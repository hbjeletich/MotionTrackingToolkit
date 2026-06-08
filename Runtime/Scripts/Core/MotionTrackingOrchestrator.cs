using UnityEngine;
using CapturyToolkit.Kinect;

/// <summary>
/// Single entry point for switching between Captury, Kinect, and MediaPipe at edit time.
/// Set activeSource in the inspector — the matching child manager activates and the others
/// deactivate. Works with OnValidate so changes take effect immediately in the editor.
///
/// Prefab layout:
///   MotionTrackingOrchestrator
///     ├── [MotionTrackingManager]        (dontDestroyOnLoad = OFF)
///     ├── [KinectMotionTrackingManager]  (dontDestroyOnLoad = OFF)
///     └── [MediaPipeMotionTrackingManager] (dontDestroyOnLoad = OFF)
///
/// Implements IMotionTrackingManager as a facade — other systems can hold a reference to
/// the orchestrator without knowing which source is active.
/// </summary>
[DisallowMultipleComponent]
public class MotionTrackingOrchestrator : MonoBehaviour, IMotionTrackingManager
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

    #endregion

    #region Private

    private IMotionTrackingManager ActiveManager => activeSource switch
    {
        MotionSource.Captury   => capturyManager,
        MotionSource.Kinect    => kinectManager,
        MotionSource.MediaPipe => mediaPipeManager,
        _                      => null
    };

    private void OnValidate() => SyncActiveState();

    private void Awake()
    {
        if (config != null) ActiveManager?.LoadConfiguration(config);
        if(config == null) Debug.LogWarning("No configuration set for MotionTrackingOrchestrator.");
        if(ActiveManager == null) Debug.LogError("No active motion tracking manager found.");
        if (dontDestroyOnLoad) DontDestroyOnLoad(gameObject);
        SyncActiveState();
    }


    private void SyncActiveState()
    {
        if (capturyManager   != null) capturyManager.gameObject.SetActive(activeSource == MotionSource.Captury);
        if (kinectManager    != null) kinectManager.gameObject.SetActive(activeSource == MotionSource.Kinect);
        if (mediaPipeManager != null) mediaPipeManager.gameObject.SetActive(activeSource == MotionSource.MediaPipe);
    }

    #endregion
}
