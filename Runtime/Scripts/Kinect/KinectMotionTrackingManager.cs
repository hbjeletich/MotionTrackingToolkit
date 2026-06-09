using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Windows.Kinect;

namespace CapturyToolkit.Kinect
{

public class KinectMotionTrackingManager : MonoBehaviour, IMotionTrackingManager
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

    private static KinectMotionTrackingManager instance;
    public static KinectMotionTrackingManager Instance => instance;

    #endregion

    #region Public Properties

    public MotionTrackingConfiguration Config => config;
    public MotionSource Source => MotionSource.Kinect;

    public bool IsBodyTracked => isBodyTracked;
    public bool IsSystemCalibrated => isSystemCalibrated;
    public bool IsCalibrating => activeCalibrationCoroutine != null;
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
    }

    void Update()
    {
        if (bodySourceManager == null) return;

        bool hasBody = UpdateBodyData();

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
        CleanupSystem();
    }

    #endregion

    #region Singleton and Configuration

    private void SetupSingleton()
    {
        if (dontDestroyOnLoad)
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            instance = this;
        }
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
    }

    private void OnBodyFound(ulong bodyId)
    {
        bool isFirstBody = !isBodyTracked;
        currentTrackedBodyId = bodyId;
        isBodyTracked = true;

        if (enableDebugLogging)
            Debug.Log($"KinectMotionTrackingManager: Body found — ID: {bodyId}");

        ResetSmoothingState();

        if (isFirstBody && !isSystemCalibrated)
        {
            if (TryLoadDefaultCalibration())
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

    // Room-scale is not yet implemented for Kinect; depth sensor positions are sensor-relative
    // and could be mapped with a sensor transform in a future pass.
    public bool SupportsRoomScale => false;
    public bool TryGetRoomPosition(out Vector3 gamePosition) { gamePosition = Vector3.zero; return false; }

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

    #endregion
}

} // namespace CapturyToolkit.Kinect