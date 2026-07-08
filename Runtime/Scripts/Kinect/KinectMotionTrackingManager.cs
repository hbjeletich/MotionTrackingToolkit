using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Windows.Kinect;

namespace CapturyToolkit.Kinect
{

public class KinectMotionTrackingManager : MonoBehaviour, IMotionTrackingManager, ICalibratableTrackingManager, IBoundaryWalkable, IRoomFrameSource
{
    #region Inspector Settings

    [Header("Configuration")]
    [SerializeField] private MotionTrackingConfiguration config;
    [SerializeField] private bool dontDestroyOnLoad = true;
    [SerializeField] private bool enableDebugLogging = true;

    [Header("Kinect Plugin")]
    [Tooltip("Reference to the BodySourceManager component from the KinectView plugin")]
    [SerializeField] private BodySourceManager bodySourceManager;

    [Header("Body Selection")]
    [Tooltip("How to pick which detected body to track")]
    [SerializeField] private BodySelectionMode bodySelection = BodySelectionMode.Closest;

    [Header("Confidence Filtering")]
    [Tooltip("Accept joints with Inferred tracking state (less accurate but avoids gaps)")]
    [SerializeField] private bool acceptInferredJoints = true;

    [Header("Joint Smoothing")]
    [Tooltip("Enable exponential smoothing on joint positions to reduce Kinect jitter")]
    [SerializeField] private bool enableSmoothing = true;

    [Tooltip("Smoothing factor for positions (0 = no smoothing / raw, 1 = frozen). Typical range: 0.3–0.6")]
    [Range(0f, 0.95f)]
    [SerializeField] private float positionSmoothingFactor = 0.5f;

    [Header("Calibration")]
    [Tooltip("If set and a matching saved calibration exists, it loads on startup instead of running " +
             "a live calibration. Leave blank to always calibrate live.")]
    [SerializeField] private string defaultCalibrationName = "";

    [Header("Room Calibration")]
    [Tooltip("If set and a matching saved room calibration exists, it loads on startup.")]
    [SerializeField] private string defaultRoomCalibrationName = "";

    [Tooltip("Seconds to wait before capturing room calibration. Stand at center, face forward.")]
    [SerializeField] private float roomCalibrationDelay = 3.0f;

    [Tooltip("Room→game uniform scale. Default 1.0 = 1:1 metres.")]
    [SerializeField] private float roomScale = 1.0f;

    #endregion

    #region Enums

    public enum BodySelectionMode
    {
        Closest,
        FirstFound
    }

    #endregion

    #region Private State

    private GameObject jointProxyRoot;
    private Dictionary<string, Transform> jointLookup = new Dictionary<string, Transform>();
    private Dictionary<string, Vector3> smoothedPositions = new Dictionary<string, Vector3>();
    private Dictionary<string, bool> jointInitialized = new Dictionary<string, bool>();

    private CapturyInput inputDevice;

    private List<MotionTrackingModule> allModules = new List<MotionTrackingModule>();

    private bool isBodyTracked = false;
    private bool isSystemCalibrated = false;
    private Coroutine activeCalibrationCoroutine = null;
    private ulong currentTrackedBodyId = 0;

    // room calibration
    private RoomCalibration activeRoomCalibration = null;
    private Coroutine activeRoomCalibrationCoroutine = null;
    private Body _currentBody = null;
    private bool _capturingBoundary = false;
    private List<Vector2> _boundaryInProgress = new List<Vector2>();
    private float _boundaryMinReliableDistance = float.MaxValue;
    private const float BOUNDARY_SAMPLE_DISTANCE = 0.1f;
    private float _boundaryLossTimer = 0f;
    private const float BOUNDARY_LOSS_DEBOUNCE = 0.3f;
    private bool _boundaryWalkPaused = false;
    private bool _boundaryHasMovedAway = false;
    private bool _boundaryLoopDetected = false;
    private const float LOOP_CLOSE_DISTANCE = 0.5f;

    /// <summary>True while a boundary walk is in progress but tracking has been lost long enough to warrant pausing.</summary>
    public bool BoundaryWalkPaused => _boundaryWalkPaused;
    /// <summary>True once the player has left and returned to their starting point — the flow controller should call StopWalk().</summary>
    public bool BoundaryLoopDetected => _boundaryLoopDetected;
    private static readonly JointType[] KeyJointsForQuality = {
        JointType.SpineBase, JointType.SpineMid,
        JointType.HipLeft, JointType.HipRight,
        JointType.KneeLeft, JointType.KneeRight,
        JointType.ShoulderLeft, JointType.ShoulderRight
    };
    private const int MIN_TRACKED_KEY_JOINTS = 6;

    private static KinectMotionTrackingManager instance;
    public static KinectMotionTrackingManager Instance => instance;

    #endregion

    #region Public Properties

    public MotionTrackingConfiguration Config => config;
    public MotionSource Source => MotionSource.Kinect;

    public bool IsBodyTracked => isBodyTracked;
    public bool IsSystemCalibrated => isSystemCalibrated;
    public bool IsCalibrating => activeCalibrationCoroutine != null;

    public event System.Action<ulong> BodyFound;
    public event System.Action BodyLost;
    public int ActiveModuleCount => allModules.Count;
    public string CurrentConfigurationName => config?.configurationName ?? "None";
    public ulong CurrentTrackedBodyId => currentTrackedBodyId;

    public bool EnableSmoothing
    {
        get => enableSmoothing;
        set => enableSmoothing = value;
    }

    public float PositionSmoothingFactor
    {
        get => positionSmoothingFactor;
        set => positionSmoothingFactor = Mathf.Clamp(value, 0f, 0.95f);
    }

    public T GetModule<T>() where T : MotionTrackingModule
    {
        return allModules.OfType<T>().FirstOrDefault();
    }

    public TorsoTrackingModule GetTorsoModule() => GetModule<TorsoTrackingModule>();
    public FootTrackingModule GetFootModule() => GetModule<FootTrackingModule>();
    public ArmTrackingModule GetArmsModule() => GetModule<ArmTrackingModule>();
    public HeadTrackingModule GetHeadModule() => GetModule<HeadTrackingModule>();
    public BalanceTrackingModule GetBalanceModule() => GetModule<BalanceTrackingModule>();

    public bool IsModuleActive<T>() where T : MotionTrackingModule
    {
        var module = GetModule<T>();
        return module != null && module.IsEnabled && module.IsCalibrated;
    }

    public bool IsTorsoModuleEnabled => IsModuleActive<TorsoTrackingModule>();
    public bool IsFootModuleEnabled => IsModuleActive<FootTrackingModule>();
    public bool IsArmsModuleEnabled => IsModuleActive<ArmTrackingModule>();
    public bool IsHeadModuleEnabled => IsModuleActive<HeadTrackingModule>();
    public bool IsBalanceModuleEnabled => IsModuleActive<BalanceTrackingModule>();

    #endregion

    #region Unity Lifecycle

    void Awake()
    {
        SetupSingleton();
    }

    void Start()
    {
        LoadDefaultConfiguration();
        InitializeInputDevice();
        CreateJointProxies();
        FindBodySourceManager();
        TryLoadDefaultRoomCalibration();
    }

    void OnEnable()
    {
        InputSystem.onDeviceChange += HandleInputDeviceChange;
    }

    void OnDisable()
    {
        InputSystem.onDeviceChange -= HandleInputDeviceChange;
    }

    // Keep inputDevice reference current when the Input System recreates device
    private void HandleInputDeviceChange(InputDevice device, InputDeviceChange change)
    {
        if (!(device is CapturyInput capturyDevice)) return;

        if (change == InputDeviceChange.Added)
            inputDevice = capturyDevice;
        else if (change == InputDeviceChange.Removed && device == inputDevice)
            inputDevice = null;
    }

    void Update()
    {
        if (bodySourceManager == null) return;

        bool hasBody = UpdateBodyData();

        if (_capturingBoundary && isBodyTracked && _currentBody != null)
            UpdateBoundaryCapture();

        if (isSystemCalibrated && inputDevice != null && isBodyTracked)
        {
            UpdateAllModules();
        }
        else
        {
            if (enableDebugLogging && Time.frameCount % 600 == 0)
            {
                Debug.Log($"KinectMotionTrackingManager: Not updating — " +
                         $"Calibrated: {isSystemCalibrated}, InputDevice: {inputDevice != null}, " +
                         $"BodyTracked: {isBodyTracked}, BodySourceManager: {bodySourceManager != null}");
            }
        }
    }

    void OnDestroy()
    {
        if (enableDebugLogging) Debug.Log("KinectMotionTrackingManager: OnDestroy()");
        if (instance == this) instance = null;
        CleanupSystem();
    }

    #endregion

    #region Singleton and Configuration

    private void SetupSingleton()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
        if (dontDestroyOnLoad)
            DontDestroyOnLoad(gameObject);
    }

    private void LoadDefaultConfiguration()
    {
        if (config != null)
        {
            LoadConfiguration(config);
        }
        else
        {
            Debug.LogWarning("KinectMotionTrackingManager: No configuration assigned, using runtime defaults");
            config = ScriptableObject.CreateInstance<MotionTrackingConfiguration>();
            config.motionSource = MotionSource.Kinect;
            LoadConfiguration(config);
        }
    }

    public void LoadConfiguration(MotionTrackingConfiguration newConfig)
    {
        if (newConfig == null)
        {
            Debug.LogError("KinectMotionTrackingManager: Cannot load null configuration!");
            return;
        }

        config = newConfig;

        if (config.motionSource == MotionSource.Kinect)
        {
            config.ApplySourceDefaults();
        }
        else if (config.motionSource != MotionSource.Custom)
        {
            Debug.LogWarning($"KinectMotionTrackingManager: Config source is '{config.motionSource}' but this manager requires Kinect. Overriding.");
            config.motionSource = MotionSource.Kinect;
            config.ApplySourceDefaults();
        }

        if (enableDebugLogging)
            Debug.Log($"KinectMotionTrackingManager: Loading configuration '{config.configurationName}'");

        InitializeModules();

        if (isSystemCalibrated)
        {
            Recalibrate();
        }

        if (enableDebugLogging)
            Debug.Log($"KinectMotionTrackingManager: Loaded '{config.configurationName}' with {allModules.Count} active modules");
    }

    public void SwapConfiguration(MotionTrackingConfiguration newConfig)
    {
        if (newConfig == null)
        {
            Debug.LogError("KinectMotionTrackingManager: Cannot swap to null configuration!");
            return;
        }

        if (enableDebugLogging)
            Debug.Log($"KinectMotionTrackingManager: Swapping configuration from '{config?.configurationName ?? "none"}' to '{newConfig.configurationName}'");

        if (activeCalibrationCoroutine != null)
        {
            StopCoroutine(activeCalibrationCoroutine);
            activeCalibrationCoroutine = null;
        }

        bool wasCalibrated = isSystemCalibrated;
        isSystemCalibrated = false;

        CleanupModules();

        config = newConfig;
        InitializeModules();

        if (wasCalibrated && jointLookup.Count > 0)
        {
            if (enableDebugLogging)
                Debug.Log("KinectMotionTrackingManager: Was previously calibrated, recalibrating with new config...");
            activeCalibrationCoroutine = StartCoroutine(CalibrateSystem());
        }

        if (enableDebugLogging)
            Debug.Log($"KinectMotionTrackingManager: Swap complete — '{config.configurationName}' with {allModules.Count} active modules");
    }

    #endregion

    #region Body Source Manager

    private void FindBodySourceManager()
    {
        if (bodySourceManager != null)
        {
            if (enableDebugLogging)
                Debug.Log("KinectMotionTrackingManager: Using assigned BodySourceManager");
            return;
        }

        bodySourceManager = FindObjectOfType<BodySourceManager>();

        if (bodySourceManager != null)
        {
            if (enableDebugLogging)
                Debug.Log("KinectMotionTrackingManager: Found BodySourceManager in scene");
        }
        else
        {
            Debug.LogError("KinectMotionTrackingManager: Could not find BodySourceManager! " +
                         "Make sure the KinectView plugin's BodySourceManager is in the scene.");
        }
    }

    #endregion

    #region Body Data Processing

    private bool UpdateBodyData()
    {
        Body[] bodies = bodySourceManager.GetData();

        if (bodies == null)
        {
            if (isBodyTracked) OnBodyLost();
            return false;
        }

        Body selectedBody = SelectBody(bodies);

        if (selectedBody == null)
        {
            if (isBodyTracked) OnBodyLost();
            return false;
        }

        if (!isBodyTracked || selectedBody.TrackingId != currentTrackedBodyId)
        {
            OnBodyFound(selectedBody.TrackingId);
        }

        ApplyBodyToProxies(selectedBody);
        return true;
    }

    private Body SelectBody(Body[] bodies)
    {
        switch (bodySelection)
        {
            case BodySelectionMode.Closest:
                return SelectClosestBody(bodies);

            case BodySelectionMode.FirstFound:
                return SelectFirstTrackedBody(bodies);

            default:
                return SelectClosestBody(bodies);
        }
    }

    private Body SelectClosestBody(Body[] bodies)
    {
        Body closest = null;
        float closestZ = float.MaxValue;

        for (int i = 0; i < bodies.Length; i++)
        {
            if (bodies[i] == null || !bodies[i].IsTracked) continue;

            float z = bodies[i].Joints[JointType.SpineBase].Position.Z;
            if (z < closestZ)
            {
                closestZ = z;
                closest = bodies[i];
            }
        }

        return closest;
    }

    private Body SelectFirstTrackedBody(Body[] bodies)
    {
        for (int i = 0; i < bodies.Length; i++)
        {
            if (bodies[i] != null && bodies[i].IsTracked)
                return bodies[i];
        }
        return null;
    }

    private void ApplyBodyToProxies(Body body)
    {
        foreach (JointType jointType in KinectJointMapper.AllJointTypes)
        {
            string jointName = KinectJointMapper.GetJointName(jointType);
            if (jointName == null) continue;

            if (!jointLookup.TryGetValue(jointName, out Transform proxy)) continue;

            Windows.Kinect.Joint kinectJoint = body.Joints[jointType];

            if (kinectJoint.TrackingState == TrackingState.NotTracked)
                continue;

            if (!acceptInferredJoints && kinectJoint.TrackingState == TrackingState.Inferred)
                continue;

            Vector3 rawPosition = new Vector3(
                kinectJoint.Position.X,
                kinectJoint.Position.Y,
                kinectJoint.Position.Z
            );

            if (enableSmoothing && jointInitialized.TryGetValue(jointName, out bool initialized) && initialized)
            {
                Vector3 smoothed = Vector3.Lerp(rawPosition, smoothedPositions[jointName], positionSmoothingFactor);
                proxy.position = smoothed;
                smoothedPositions[jointName] = smoothed;
            }
            else
            {
                proxy.position = rawPosition;
                smoothedPositions[jointName] = rawPosition;
                jointInitialized[jointName] = true;
            }

            JointOrientation orientation = body.JointOrientations[jointType];
            if (kinectJoint.TrackingState == TrackingState.Tracked)
            {
                proxy.rotation = new Quaternion(
                    orientation.Orientation.X,
                    orientation.Orientation.Y,
                    orientation.Orientation.Z,
                    orientation.Orientation.W
                );
            }
        }

        _currentBody = body;
    }

    private void OnBodyFound(ulong bodyId)
    {
        bool isFirstBody = !isBodyTracked;
        currentTrackedBodyId = bodyId;
        isBodyTracked = true;
        BodyFound?.Invoke(bodyId);

        if (enableDebugLogging)
            Debug.Log($"KinectMotionTrackingManager: Body found — ID: {bodyId}");

        ResetSmoothingState();

        if (isFirstBody && !isSystemCalibrated)
        {
            // Resolve joints before any calibration path so CalibrationHooks and LoadCalibration
            // can both call SetCalibration on modules with valid resolvedJoints already populated.
            foreach (var module in allModules)
                module.PrepareForTracking();

            bool externalCalibration = CalibrationHooks.OnSkeletonReadyForCalibration?.Invoke(this) ?? false;

            if (externalCalibration)
            {
                isSystemCalibrated = true;
                if (enableDebugLogging)
                    Debug.Log("KinectMotionTrackingManager: External calibration provided via hook — skipping auto-calibration");
            }
            else if (TryLoadDefaultCalibration())
            {
                isSystemCalibrated = true;
                if (enableDebugLogging)
                    Debug.Log($"KinectMotionTrackingManager: Loaded saved calibration '{defaultCalibrationName}', skipping live calibration");
            }
            else
            {
                activeCalibrationCoroutine = StartCoroutine(CalibrateSystem());
            }
        }
    }

    private bool TryLoadDefaultCalibration()
    {
        if (string.IsNullOrEmpty(defaultCalibrationName)) return false;
        if (!CalibrationStore.Exists(defaultCalibrationName)) return false;
        return LoadCalibration(defaultCalibrationName);
    }

    private void OnBodyLost()
    {
        if (enableDebugLogging)
            Debug.Log($"KinectMotionTrackingManager: Body lost — ID: {currentTrackedBodyId}");

        isBodyTracked = false;
        currentTrackedBodyId = 0;
        _currentBody = null;
        BodyLost?.Invoke();
    }

    #endregion

    #region Joint Proxy System

    private void CreateJointProxies()
    {
        jointProxyRoot = new GameObject("KinectJointProxies");
        jointProxyRoot.transform.SetParent(transform);

        jointLookup.Clear();
        smoothedPositions.Clear();
        jointInitialized.Clear();

        foreach (JointType jointType in KinectJointMapper.AllJointTypes)
        {
            string jointName = KinectJointMapper.GetJointName(jointType);
            if (jointName == null) continue;

            GameObject proxyObj = new GameObject(jointName);
            proxyObj.transform.SetParent(jointProxyRoot.transform);
            proxyObj.transform.position = Vector3.zero;
            proxyObj.transform.rotation = Quaternion.identity;

            jointLookup[jointName] = proxyObj.transform;
            smoothedPositions[jointName] = Vector3.zero;
            jointInitialized[jointName] = false;
        }

        if (enableDebugLogging)
            Debug.Log($"KinectMotionTrackingManager: Created {jointLookup.Count} joint proxies");
    }

    private void ResetSmoothingState()
    {
        foreach (string jointName in jointInitialized.Keys.ToList())
        {
            jointInitialized[jointName] = false;
        }
    }

    #endregion

    #region Input Device

    private void InitializeInputDevice()
    {
        if (enableDebugLogging) Debug.Log("KinectMotionTrackingManager: Initializing input device...");

        inputDevice = InputSystem.GetDevice<CapturyInput>();
        if (inputDevice == null)
        {
            CapturyInput.Register();
            inputDevice = InputSystem.AddDevice<CapturyInput>();
            if (enableDebugLogging) Debug.Log("KinectMotionTrackingManager: Created new input device");
        }
        else
        {
            if (enableDebugLogging) Debug.Log("KinectMotionTrackingManager: Using existing input device");
        }
    }

    #endregion

    #region Module Lifecycle

    private void InitializeModules()
    {
        if (enableDebugLogging) Debug.Log("KinectMotionTrackingManager: Initializing modules...");

        CleanupModules();

        GameObject moduleParent = new GameObject("TrackingModules");
        moduleParent.transform.SetParent(transform);

        foreach (var moduleConfig in config.modules)
        {
            if (moduleConfig == null || !moduleConfig.enabled) continue;

            MotionTrackingModule module = moduleConfig.CreateModule(moduleParent);
            if (module != null)
            {
                module.Initialize(this);
                allModules.Add(module);

                if (enableDebugLogging)
                    Debug.Log($"KinectMotionTrackingManager: Initialized {module.GetType().Name} — Enabled: {module.IsEnabled}");
            }
        }
    }

    private void UpdateAllModules()
    {
        // inputDevice may have been removed, fall back to GetDevice as a safety net.
        if (inputDevice == null || !inputDevice.added)
        {
            inputDevice = InputSystem.GetDevice<CapturyInput>();
            if (inputDevice == null) return;
        }

        CapturyInputState state = new CapturyInputState();

        foreach (var module in allModules)
        {
            module.UpdateTracking(ref state);
        }

        InputSystem.QueueStateEvent(inputDevice, state);
    }

    private void CleanupModules()
    {
        foreach (var module in allModules)
        {
            if (module != null && module.gameObject != null)
            {
                DestroyImmediate(module.gameObject.transform.parent.gameObject);
                break;
            }
        }
        allModules.Clear();
    }

    #endregion

    #region Calibration

    private IEnumerator CalibrateSystem()
    {
        if (enableDebugLogging)
            Debug.Log($"KinectMotionTrackingManager: Starting calibration in {config.calibrationDelay}s...");

        yield return new WaitForSeconds(config.calibrationDelay);

        if (!isBodyTracked)
        {
            Debug.LogWarning("KinectMotionTrackingManager: Body lost during calibration delay, aborting");
            activeCalibrationCoroutine = null;
            yield break;
        }

        if (enableDebugLogging)
            Debug.Log($"KinectMotionTrackingManager: Calibrating {allModules.Count} modules...");

        foreach (var module in allModules)
        {
            if (module.HasRequiredJoints())
            {
                if (enableDebugLogging)
                    Debug.Log($"KinectMotionTrackingManager: Calibrating {module.GetType().Name}...");
                module.Calibrate();
            }
            else
            {
                Debug.LogWarning($"KinectMotionTrackingManager: {module.GetType().Name} missing required joints!");
            }
        }

        isSystemCalibrated = true;
        activeCalibrationCoroutine = null;

        if (enableDebugLogging)
            Debug.Log("KinectMotionTrackingManager: System calibrated and ready!");
    }

    public void SaveCalibration(string calibrationName)
    {
        var bundle = new CalibrationBundle
        {
            calibrationName = calibrationName,
            source = Source.ToString(),
            timestamp = Time.time
        };

        foreach (var module in allModules)
        {
            if (module == null) continue;
            string json = module.SerializeCalibration();
            if (string.IsNullOrEmpty(json)) continue;
            bundle.entries.Add(new CalibrationBundleEntry
            {
                moduleType = module.GetType().Name,
                json = json
            });
        }

        CalibrationStore.Save(bundle);
    }

    public bool LoadCalibration(string calibrationName)
    {
        var bundle = CalibrationStore.Load(calibrationName);
        if (bundle == null)
        {
            Debug.LogWarning($"KinectMotionTrackingManager: No saved calibration '{calibrationName}'");
            return false;
        }

        if (bundle.source != Source.ToString())
        {
            Debug.LogWarning($"KinectMotionTrackingManager: Calibration '{calibrationName}' was captured on " +
                             $"source '{bundle.source}' but active source is '{Source}'. " +
                             $"Joint positions may not transfer meaningfully.");
        }

        foreach (var module in allModules)
        {
            if (module == null) continue;
            var entry = bundle.entries.Find(e => e.moduleType == module.GetType().Name);
            if (entry == null) continue;
            module.DeserializeCalibration(entry.json);
            module.PrepareForTracking();
        }

        return true;
    }

    #endregion

    #region Cleanup

    private void CleanupSystem()
    {
        if (activeRoomCalibrationCoroutine != null) StopCoroutine(activeRoomCalibrationCoroutine);
        _capturingBoundary = false;

        CleanupModules();

        if (jointProxyRoot != null)
        {
            DestroyImmediate(jointProxyRoot);
            jointProxyRoot = null;
        }

        if (inputDevice != null && !dontDestroyOnLoad)
        {
            InputSystem.RemoveDevice(inputDevice);
        }

        if (enableDebugLogging)
            Debug.Log("KinectMotionTrackingManager: Cleanup complete");
    }

    #endregion

    #region IMotionTrackingManager

    public Transform GetJointByName(string jointName)
    {
        jointLookup.TryGetValue(jointName, out Transform joint);
        return joint;
    }

    public bool SupportsRoomScale => isBodyTracked && activeRoomCalibration != null;
    public bool HasRoomCalibration => activeRoomCalibration != null;

    public bool TryGetRoomPosition(out Vector3 gamePosition)
    {
        if (!isBodyTracked || activeRoomCalibration == null ||
            !jointLookup.TryGetValue("SpineBase", out Transform spineBase))
        { gamePosition = Vector3.zero; return false; }
        gamePosition = activeRoomCalibration.GetRoomToGame().MultiplyPoint3x4(spineBase.position);
        return true;
    }

    public bool HasRoomBounds => activeRoomCalibration?.HasBoundary ?? false;
    public Vector3[] GetRoomBoundary() =>
        activeRoomCalibration?.GetGameSpaceBoundary() ?? System.Array.Empty<Vector3>();
    public float RoomMinTrackingDistance =>
        (activeRoomCalibration != null && activeRoomCalibration.HasMinTrackingDistance)
            ? activeRoomCalibration.minTrackingDistance * activeRoomCalibration.scale
            : 0f;

    #endregion

    #region IRoomFrameSource

    public event Action<RoomFrame> OnFrameCaptured;

    public bool HasRoomFrame => activeRoomCalibration != null;

    public RoomFrame CurrentFrame
    {
        get
        {
            if (activeRoomCalibration == null) return null;
            return new RoomFrame
            {
                originOffset       = activeRoomCalibration.originOffset,
                yawDegrees         = activeRoomCalibration.yawDegrees,
                floorNormalY       = 1f,
                floorPlaneDistance = -activeRoomCalibration.floorHeight,
                scale              = activeRoomCalibration.scale,
            };
        }
    }

    public void StartFrameCapture(FrameCapturePolicy policy)
    {
        StartRoomCalibration();
    }

    #endregion

    #region Public API

    public void Recalibrate()
    {
        if (enableDebugLogging) Debug.Log("KinectMotionTrackingManager: Manual recalibration requested");

        if (activeCalibrationCoroutine != null)
        {
            StopCoroutine(activeCalibrationCoroutine);
            activeCalibrationCoroutine = null;
        }

        isSystemCalibrated = false;
        activeCalibrationCoroutine = StartCoroutine(CalibrateSystem());
    }

    public bool LoadPreviousCalibration()
    {
        bool allRestored = true;
        foreach (var module in allModules)
        {
            if (!module.LoadPreviousCalibration())
            {
                allRestored = false;
                Debug.LogWarning($"KinectMotionTrackingManager: {module.GetType().Name} had no previous calibration");
            }
        }

        if (enableDebugLogging)
            Debug.Log($"KinectMotionTrackingManager: LoadPreviousCalibration — all restored: {allRestored}");

        return allRestored;
    }

    public void CalibrateRoom()
    {
        if (!isBodyTracked)
        { Debug.LogWarning("KinectMotionTrackingManager: CalibrateRoom — no body tracked."); return; }
        if (activeRoomCalibrationCoroutine != null) StopCoroutine(activeRoomCalibrationCoroutine);
        activeRoomCalibrationCoroutine = StartCoroutine(CaptureRoomCalibration(defaultRoomCalibrationName));
    }

    public void StartRoomCalibration()
    {
        if (activeRoomCalibrationCoroutine != null) StopCoroutine(activeRoomCalibrationCoroutine);
        activeRoomCalibrationCoroutine = StartCoroutine(CaptureRoomCalibration(defaultRoomCalibrationName));
    }

    public void SaveRoomCalibration(string name)
    {
        if (activeRoomCalibration == null)
        { Debug.LogWarning("KinectMotionTrackingManager: No active room calibration to save."); return; }
        activeRoomCalibration.calibrationName = name;
        RoomCalibrationStore.Save(activeRoomCalibration);
    }

    public bool LoadRoomCalibration(string name)
    {
        var cal = RoomCalibrationStore.Load(name);
        if (cal == null)
        { Debug.LogWarning($"KinectMotionTrackingManager: No room calibration '{name}' found."); return false; }
        if (cal.source != Source.ToString())
            Debug.LogWarning($"KinectMotionTrackingManager: Room calibration '{name}' captured on '{cal.source}', current source is '{Source}'.");
        activeRoomCalibration = cal;
        if (enableDebugLogging) Debug.Log($"KinectMotionTrackingManager: Loaded room calibration '{name}'");
        return true;
    }

    public void StartBoundaryWalk()
    {
        if (!isBodyTracked)
        { Debug.LogWarning("KinectMotionTrackingManager: StartBoundaryWalk — no body tracked."); return; }
        if (activeRoomCalibration == null)
        { Debug.LogWarning("KinectMotionTrackingManager: StartBoundaryWalk — run CalibrateRoom first."); return; }
        _boundaryInProgress.Clear();
        _boundaryMinReliableDistance = float.MaxValue;
        _boundaryLossTimer = 0f;
        _boundaryWalkPaused = false;
        _boundaryHasMovedAway = false;
        _boundaryLoopDetected = false;
        _capturingBoundary = true;
        if (enableDebugLogging) Debug.Log("KinectMotionTrackingManager: Boundary walk started — walk the perimeter.");
    }

    public void StopBoundaryWalk()
    {
        _capturingBoundary = false;
        _boundaryLossTimer = 0f;
        _boundaryWalkPaused = false;
        _boundaryHasMovedAway = false;
        _boundaryLoopDetected = false;
        if (_boundaryInProgress.Count < 3)
        { Debug.LogWarning($"KinectMotionTrackingManager: Boundary walk too short ({_boundaryInProgress.Count} points)."); return; }
        activeRoomCalibration.boundaryPoints = _boundaryInProgress.ToArray();
        if (_boundaryMinReliableDistance < float.MaxValue)
            activeRoomCalibration.minTrackingDistance = _boundaryMinReliableDistance;
        _boundaryInProgress.Clear();
        _boundaryMinReliableDistance = float.MaxValue;
        if (enableDebugLogging)
            Debug.Log($"KinectMotionTrackingManager: Boundary captured — {activeRoomCalibration.boundaryPoints.Length} points, " +
                      $"minTrackingDistance={activeRoomCalibration.minTrackingDistance:F2}m");
    }

    public void SetBoundaryPoints(Vector3[] gameSpacePoints)
    {
        if (activeRoomCalibration == null || gameSpacePoints == null || gameSpacePoints.Length < 3)
        { Debug.LogWarning("KinectMotionTrackingManager: SetBoundaryPoints — need room calibration and at least 3 points."); return; }
        var inv = activeRoomCalibration.GetRoomToGame().inverse;
        var pts = new Vector2[gameSpacePoints.Length];
        for (int i = 0; i < gameSpacePoints.Length; i++)
        {
            var roomPos = inv.MultiplyPoint3x4(gameSpacePoints[i]);
            pts[i] = new Vector2(roomPos.x, roomPos.z);
        }
        activeRoomCalibration.boundaryPoints = pts;
    }

    public bool MergeSavedBoundary(string calibrationName)
    {
        if (activeRoomCalibration == null) return false;
        var saved = RoomCalibrationStore.Load(calibrationName);
        if (saved == null || !saved.HasBoundary) return false;
        activeRoomCalibration.boundaryPoints = saved.boundaryPoints;
        activeRoomCalibration.minTrackingDistance = saved.minTrackingDistance;
        if (enableDebugLogging)
            Debug.Log($"KinectMotionTrackingManager: Merged saved boundary '{calibrationName}' — {saved.boundaryPoints.Length} points.");
        return true;
    }

    #endregion

    #region Room Calibration

    private void TryLoadDefaultRoomCalibration()
    {
        if (string.IsNullOrEmpty(defaultRoomCalibrationName)) return;
        if (!RoomCalibrationStore.Exists(defaultRoomCalibrationName)) return;
        LoadRoomCalibration(defaultRoomCalibrationName);
    }

    private IEnumerator CaptureRoomCalibration(string saveName)
    {
        if (enableDebugLogging)
            Debug.Log($"KinectMotionTrackingManager: Room calibration in {roomCalibrationDelay}s — stand at center, face forward...");

        yield return new WaitForSeconds(roomCalibrationDelay);

        if (!isBodyTracked)
        {
            if (enableDebugLogging)
                Debug.Log("KinectMotionTrackingManager: CaptureRoomCalibration — waiting for body tracking...");
            yield return new WaitUntil(() => isBodyTracked);
        }
        if (!jointLookup.TryGetValue("SpineBase", out Transform spineBase))
        {
            Debug.LogWarning("KinectMotionTrackingManager: CaptureRoomCalibration — SpineBase joint not found, aborting.");
            activeRoomCalibrationCoroutine = null;
            yield break;
        }

        Vector3 origin = spineBase.position;
        float floorHeight = GetFloorHeightAtPosition(origin.x, origin.z);
        float yaw = spineBase.rotation.eulerAngles.y;

        var cal = new RoomCalibration
        {
            calibrationName = string.IsNullOrEmpty(saveName) ? "room" : saveName,
            source = Source.ToString(),
            timestamp = Time.time,
            originOffset = origin,
            yawDegrees = yaw,
            floorHeight = floorHeight,
            scale = roomScale
        };

        activeRoomCalibration = cal;
        if (!string.IsNullOrEmpty(saveName))
            RoomCalibrationStore.Save(cal);

        activeRoomCalibrationCoroutine = null;

        if (enableDebugLogging)
            Debug.Log($"KinectMotionTrackingManager: Room calibration done — origin={origin}, yaw={yaw:F1}°, floor={floorHeight:F3}m");

        OnFrameCaptured?.Invoke(CurrentFrame);
    }

    private float GetFloorHeightAtPosition(float x, float z)
    {
        if (bodySourceManager != null && bodySourceManager.FloorClipPlaneValid)
        {
            var fp = bodySourceManager.FloorClipPlane;
            // plane: A*x + B*y + C*z + D = 0 → y = -(A*x + C*z + D) / B
            return -(fp.x * x + fp.z * z + fp.w) / fp.y;
        }
        // fallback: minimum ankle height
        float la = jointLookup.TryGetValue("AnkleLeft",  out Transform tl) ? tl.position.y : 0f;
        float ra = jointLookup.TryGetValue("AnkleRight", out Transform tr) ? tr.position.y : 0f;
        return Mathf.Min(la, ra);
    }

    private void UpdateBoundaryCapture()
    {
        if (!jointLookup.TryGetValue("Head", out Transform head)) return;
        var current = new Vector2(head.position.x, head.position.z);

        if (!IsCurrentTrackingReliable())
        {
            _boundaryLossTimer += Time.deltaTime;
            if (_boundaryLossTimer >= BOUNDARY_LOSS_DEBOUNCE)
                _boundaryWalkPaused = true;
            return;
        }

        _boundaryLossTimer = 0f;
        _boundaryWalkPaused = false;

        if (_boundaryInProgress.Count == 0 ||
            Vector2.Distance(current, _boundaryInProgress[_boundaryInProgress.Count - 1]) >= BOUNDARY_SAMPLE_DISTANCE)
            _boundaryInProgress.Add(current);

        if (activeRoomCalibration != null)
        {
            var originXZ = new Vector2(activeRoomCalibration.originOffset.x, activeRoomCalibration.originOffset.z);
            float dist = Vector2.Distance(current, originXZ);
            if (dist < _boundaryMinReliableDistance)
                _boundaryMinReliableDistance = dist;
        }

        // loop closure detection
        if (_boundaryInProgress.Count >= 3)
        {
            float distFromStart = Vector2.Distance(current, _boundaryInProgress[0]);
            if (!_boundaryHasMovedAway)
            {
                if (distFromStart > LOOP_CLOSE_DISTANCE * 2f)
                    _boundaryHasMovedAway = true;
            }
            else if (distFromStart <= LOOP_CLOSE_DISTANCE)
            {
                _boundaryLoopDetected = true;
            }
        }
    }

    public bool IsCurrentTrackingReliable()
    {
        if (_currentBody == null) return false;
        int count = 0;
        foreach (var jt in KeyJointsForQuality)
            if (_currentBody.Joints[jt].TrackingState == TrackingState.Tracked)
                count++;
        return count >= MIN_TRACKED_KEY_JOINTS;
    }

    #endregion
}

} // namespace CapturyToolkit.Kinect
