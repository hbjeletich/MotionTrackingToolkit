using System;
using UnityEngine;
using CapturyToolkit.Kinect;

/// <summary>
/// Single entry point for switching between Captury, Kinect, MediaPipe, and OAK-D at edit time.
/// Set activeSource in the inspector — the matching child manager activates and the others
/// deactivate. Works with OnValidate so changes take effect immediately in the editor.
///
/// Prefab layout:
///   MotionTrackingOrchestrator
///     ├── [MotionTrackingManager]        (dontDestroyOnLoad = OFF)
///     ├── [KinectMotionTrackingManager]  (dontDestroyOnLoad = OFF)
///     ├── [MediaPipeMotionTrackingManager] (dontDestroyOnLoad = OFF)
///     └── [OakDMotionTrackingManager]    (dontDestroyOnLoad = OFF)
///
/// Implements IMotionTrackingManager as a facade — other systems can hold a reference to
/// the orchestrator without knowing which source is active.
///
/// Source override priority (highest → lowest):
///   1. GlobalSourceOverride (set in code, e.g. from a bootstrap scene)
///   2. PlayerPrefs "MTM.SourceOverride" (set via Tools > Motion Tracking menu or SetGlobalSource)
///   3. Inspector activeSource value
///
/// Scene-to-scene handoff: if a DontDestroyOnLoad orchestrator already exists when a new scene
/// loads, the newcomer hands its config to the survivor and destroys itself. Source carries
/// forward; each scene's module config is respected.
/// </summary>

[DisallowMultipleComponent]
public class MotionTrackingOrchestrator : MonoBehaviour, IMotionTrackingManager, IRoomFrameSource, ITrackableRegionProvider
{
    [Header("Source")]
    [SerializeField] private MotionSource activeSource = MotionSource.Captury;

    [Header("Configuration")]
    [Tooltip("If set, applied to the active manager on Start. Leave blank to use each manager's own config.")]
    [SerializeField] private MotionTrackingConfiguration config;

    [Header("Lifecycle")]
    [SerializeField] private bool dontDestroyOnLoad = true;

    [Header("Managers")]
    [SerializeField] private MotionTrackingManager capturyManager;
    [SerializeField] private KinectMotionTrackingManager kinectManager;
    [SerializeField] private MediaPipeMotionTrackingManager mediaPipeManager;
    [SerializeField] private OakDMotionTrackingManager oakDManager;

    // ── Static source override ────────────────────────────────────────────────
    // Set this in code (e.g. bootstrap scene) to override all Orchestrators
    // without touching any scene files. Backed by PlayerPrefs for persistence
    // across play sessions. Call SetGlobalSource / ClearGlobalSource to manage.
    public static MotionSource? GlobalSourceOverride = null;
    private const string PrefKey = "MTM.SourceOverride";

    // Singleton — tracks the currently live (potentially DDOL) orchestrator.
    private static MotionTrackingOrchestrator _instance;
    public static MotionTrackingOrchestrator Instance => _instance;

    #region IMotionTrackingManager

    public MotionTrackingConfiguration Config => config != null ? config : ActiveManager?.Config;
    public MotionSource Source => activeSource;
    public Transform GetJointByName(string name) => ActiveManager?.GetJointByName(name);

    public void LoadConfiguration(MotionTrackingConfiguration newConfig)
    {
        config = newConfig;
        ActiveManager?.LoadConfiguration(newConfig);
    }

    public void Recalibrate() => ActiveManager?.Recalibrate();

    public void SaveCalibration(string calibrationName) => ActiveManager?.SaveCalibration(calibrationName);

    public bool LoadCalibration(string calibrationName) => ActiveManager?.LoadCalibration(calibrationName) ?? false;

    public bool SupportsRoomScale => ActiveManager?.SupportsRoomScale ?? false;

    public bool TryGetRoomPosition(out Vector3 gamePosition)
    {
        if (ActiveManager == null) { gamePosition = Vector3.zero; return false; }
        return ActiveManager.TryGetRoomPosition(out gamePosition);
    }

    public bool HasRoomBounds => ActiveManager?.HasRoomBounds ?? false;
    public Vector3[] GetRoomBoundary() => ActiveManager?.GetRoomBoundary() ?? System.Array.Empty<Vector3>();
    public float RoomMinTrackingDistance => ActiveManager?.RoomMinTrackingDistance ?? 0f;

    public bool IsTracked => activeSource switch
    {
        MotionSource.Kinect    => kinectManager    != null && kinectManager.IsBodyTracked,
        MotionSource.MediaPipe => mediaPipeManager != null && mediaPipeManager.HasReceivedLandmarks,
        MotionSource.OakD      => oakDManager      != null && oakDManager.HasReceivedLandmarks,
        MotionSource.Captury   => capturyManager   != null && capturyManager.IsSystemCalibrated,
        _                      => false
    };

    public bool IsTrackingReliable() => activeSource switch
    {
        MotionSource.Kinect    => kinectManager    != null && kinectManager.IsCurrentTrackingReliable(),
        MotionSource.MediaPipe => mediaPipeManager != null && mediaPipeManager.IsTrackingReliable(),
        MotionSource.OakD      => oakDManager      != null && oakDManager.IsTrackingReliable(),
        MotionSource.Captury   => capturyManager   != null && capturyManager.IsSystemCalibrated,
        _                      => false
    };

    public IMotionTrackingManager ActiveManager => activeSource switch
    {
        MotionSource.Captury   => capturyManager,
        MotionSource.Kinect    => kinectManager,
        MotionSource.MediaPipe => mediaPipeManager,
        MotionSource.OakD      => oakDManager,
        _                      => null
    };

    #endregion

    #region IRoomFrameSource

    public event Action<RoomFrame> OnFrameCaptured;

    private Action<RoomFrame> _frameCapturedRelay;

    public bool HasRoomFrame => (ActiveManager as IRoomFrameSource)?.HasRoomFrame ?? false;

    public RoomFrame CurrentFrame => (ActiveManager as IRoomFrameSource)?.CurrentFrame;

    public void StartFrameCapture(FrameCapturePolicy policy) =>
        (ActiveManager as IRoomFrameSource)?.StartFrameCapture(policy);

    #endregion

    #region ITrackableRegionProvider

    public bool HasTrackableRegion =>
        (ActiveManager as ITrackableRegionProvider)?.HasTrackableRegion ?? false;

    public TrackableRegionSource RegionSource =>
        (ActiveManager as ITrackableRegionProvider)?.RegionSource ?? TrackableRegionSource.None;

    public Vector2[] GetTrackableRegion() =>
        (ActiveManager as ITrackableRegionProvider)?.GetTrackableRegion();

    #endregion

    #region Private

    private void OnValidate() => SyncActiveState();

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            // A persisted orchestrator already exists from a previous scene.
            // Inherit its source so it carries forward, hand off our config so the
            // survivor picks up this scene's module setup, then self-destruct.
            activeSource = _instance.activeSource;
            if (config != null)
            {
                _instance.config = config;
                _instance.UpdateConfigMotionSource();
                _instance.config.ApplySourceDefaults();
                _instance.ActiveManager?.LoadConfiguration(_instance.config);
                _instance.SyncActiveState();
            }
            Destroy(gameObject);
            return;
        }

        // Apply dev source override: static field → PlayerPrefs → inspector value.
        if (GlobalSourceOverride.HasValue)
        {
            activeSource = GlobalSourceOverride.Value;
        }
        else
        {
            int stored = PlayerPrefs.GetInt(PrefKey, -1);
            if (stored >= 0 && System.Enum.IsDefined(typeof(MotionSource), stored))
                activeSource = (MotionSource)stored;
        }

        _instance = this;

        UpdateConfigMotionSource();
        if (config != null) ActiveManager?.LoadConfiguration(config);
        if (config == null) Debug.LogWarning("[MotionTrackingOrchestrator] No configuration set.");
        if (ActiveManager == null) Debug.LogError("[MotionTrackingOrchestrator] No active manager found.");
        if (dontDestroyOnLoad) DontDestroyOnLoad(gameObject);
        SyncActiveState();

        _frameCapturedRelay = frame => OnFrameCaptured?.Invoke(frame);
        if (ActiveManager is IRoomFrameSource src)
            src.OnFrameCaptured += _frameCapturedRelay;
    }

    private void OnDestroy()
    {
        if (_frameCapturedRelay != null && ActiveManager is IRoomFrameSource src)
            src.OnFrameCaptured -= _frameCapturedRelay;
        if (_instance == this) _instance = null;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public static void SetGlobalSource(MotionSource source)
    {
        GlobalSourceOverride = source;
        PlayerPrefs.SetInt(PrefKey, (int)source);
        PlayerPrefs.Save();
    }
    public static void ClearGlobalSource()
    {
        GlobalSourceOverride = null;
        PlayerPrefs.DeleteKey(PrefKey);
        PlayerPrefs.Save();
    }


    private void SyncActiveState()
    {
        if (capturyManager   != null) capturyManager.gameObject.SetActive(activeSource == MotionSource.Captury);
        if (kinectManager    != null) kinectManager.gameObject.SetActive(activeSource == MotionSource.Kinect);
        if (mediaPipeManager != null) mediaPipeManager.gameObject.SetActive(activeSource == MotionSource.MediaPipe);
        if (oakDManager      != null) oakDManager.gameObject.SetActive(activeSource == MotionSource.OakD);
        UpdateConfigMotionSource();
    }

    private void UpdateConfigMotionSource()
    {
        if (config == null) return;
        if (config.motionSource != activeSource)
        {
            Debug.LogWarning($"[MotionTrackingOrchestrator] Config motion source ({config.motionSource}) " +
                             $"does not match active source ({activeSource}). Updating config.");
            config.motionSource = activeSource;
        }
    }
    #endregion
}
