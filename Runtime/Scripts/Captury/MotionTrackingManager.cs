using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Captury;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

public class MotionTrackingManager : MonoBehaviour, IMotionTrackingManager, ICalibratableTrackingManager
{
    [Header("Configuration")]
    [SerializeField] private MotionTrackingConfiguration config;
    [SerializeField] private bool dontDestroyOnLoad = true;
    [SerializeField] private bool enableDebugLogging = true;

    [Header("Calibration")]
    [Tooltip("If set and a matching saved calibration exists, it loads on startup instead of running " +
             "a live calibration. Leave blank to always calibrate live.")]
    [SerializeField] private string defaultCalibrationName = "";

    // core components
    private CapturyInput capturyInput;
    private CapturyNetworkPlugin networkPlugin;
    private Dictionary<string, Transform> jointLookup = new Dictionary<string, Transform>();

    // modules
    private List<MotionTrackingModule> allModules = new List<MotionTrackingModule>();

    // state
    private bool isSystemCalibrated = false;
    private Coroutine activeCalibrationCoroutine = null;

    // singleton
    private static MotionTrackingManager instance;
    public static MotionTrackingManager Instance => instance;

    // public config
    public MotionTrackingConfiguration Config => config;
    public MotionSource Source => MotionSource.Captury;
    // Room-scale is not yet implemented for Captury; Captury joint positions are already
    // world-space and could drive this in a future pass.
    public bool SupportsRoomScale => false;
    public bool TryGetRoomPosition(out Vector3 gamePosition) { gamePosition = Vector3.zero; return false; }
    public bool HasRoomBounds => false;
    public Vector3[] GetRoomBoundary() => System.Array.Empty<Vector3>();
    public float RoomMinTrackingDistance => 0f;

    #region Awake, Start, Update, Destroy

    void Awake()
    {
        SetupSingleton();
    }

    void Start()
    {
        LoadDefaultConfiguration();
        InitializeCapturyInput();
        FindNetworkPlugin();
    }

    void Update()
    {
        if (isSystemCalibrated && capturyInput != null)
        {
            UpdateAllModules();
        }
        else
        {
            if (enableDebugLogging && Time.frameCount % 600 == 0)
            {
                Debug.Log($"MotionTrackingManager: Not updating modules — Calibrated: {isSystemCalibrated}, CapturyInput present: {capturyInput != null}");
            }
        }
    }

    void OnDestroy()
    {
        if (enableDebugLogging) Debug.Log("MotionTrackingManager: OnDestroy() called");
        CleanupSystem();
    }

    #endregion

    #region Setup and Configuration

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

    public void LoadConfiguration(MotionTrackingConfiguration newConfig)
    {
        if (newConfig == null)
        {
            Debug.LogError("MotionTrackingManager: Cannot load null configuration!");
            return;
        }

        config = newConfig;

        if (enableDebugLogging)
            Debug.Log($"MotionTrackingManager: Loading configuration '{config.configurationName}'");

        InitializeModules();

        if (isSystemCalibrated)
        {
            Recalibrate();
        }

        if (enableDebugLogging)
            Debug.Log($"MotionTrackingManager: Loaded configuration '{config.configurationName}' with {allModules.Count} active modules");
    }

    private void LoadDefaultConfiguration()
    {
        if (config != null)
        {
            LoadConfiguration(config);
        }
        else
        {
            Debug.LogWarning("MotionTrackingManager: No configuration assigned, using runtime defaults");
            config = ScriptableObject.CreateInstance<MotionTrackingConfiguration>();
            LoadConfiguration(config);
        }
    }

    // iterates the config's module list and calls each config's CreateModule()
    private void InitializeModules()
    {
        if (enableDebugLogging) Debug.Log("MotionTrackingManager: Initializing modules...");

        allModules.Clear();

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
                    Debug.Log($"MotionTrackingManager: Initialized {module.GetType().Name} — Enabled: {module.IsEnabled}");
            }
        }
    }

    private void InitializeCapturyInput()
    {
        if (enableDebugLogging) Debug.Log("MotionTrackingManager: Initializing CapturyInput...");

        capturyInput = InputSystem.GetDevice<CapturyInput>();
        if (capturyInput == null)
        {
            CapturyInput.Register();
            capturyInput = InputSystem.AddDevice<CapturyInput>();
            if (enableDebugLogging) Debug.Log("MotionTrackingManager: Created new CapturyInput device");
        }
        else
        {
            if (enableDebugLogging) Debug.Log("MotionTrackingManager: Using existing CapturyInput device");
        }
    }

    private void FindNetworkPlugin()
    {
        if (enableDebugLogging) Debug.Log("MotionTrackingManager: Looking for CapturyNetworkPlugin...");

        networkPlugin = FindObjectOfType<CapturyNetworkPlugin>();
        if (networkPlugin != null)
        {
            networkPlugin.SkeletonFound -= OnSkeletonFound;
            networkPlugin.SkeletonFound += OnSkeletonFound;
            if (enableDebugLogging) Debug.Log("MotionTrackingManager: Connected to CapturyNetworkPlugin");
        }
        else
        {
            Debug.LogError("MotionTrackingManager: Could not find CapturyNetworkPlugin! Make sure it exists in the scene.");
        }
    }

    #endregion

    #region Skeleton and Calibration

    private void OnSkeletonFound(CapturySkeleton skeleton)
    {
        if (enableDebugLogging) Debug.Log("MotionTrackingManager: Skeleton found, setting up...");
        skeleton.OnSkeletonSetupComplete += OnSkeletonSetupComplete;
    }

    private void OnSkeletonSetupComplete(CapturySkeleton skeleton)
    {
        if (enableDebugLogging) Debug.Log("MotionTrackingManager: Skeleton setup complete, building joint lookup...");
        BuildJointLookup(skeleton);

        // Resolve joints before any calibration path so CalibrationHooks and LoadCalibration
        // can both call SetCalibration on modules with valid resolvedJoints already populated.
        foreach (var module in allModules)
            module.PrepareForTracking();

        bool externalCalibration = CalibrationHooks.OnSkeletonReadyForCalibration?.Invoke(this) ?? false;

        if (externalCalibration)
        {
            isSystemCalibrated = true;
            if (enableDebugLogging)
                Debug.Log("MotionTrackingManager: External calibration provided via hook — skipping auto-calibration");
        }
        else if (TryLoadDefaultCalibration())
        {
            isSystemCalibrated = true;
            if (enableDebugLogging)
                Debug.Log($"MotionTrackingManager: Loaded saved calibration '{defaultCalibrationName}', skipping live calibration");
        }
        else
        {
            activeCalibrationCoroutine = StartCoroutine(CalibrateSystem());
        }
    }

    private bool TryLoadDefaultCalibration()
    {
        if (string.IsNullOrEmpty(defaultCalibrationName)) return false;
        if (!CalibrationStore.Exists(defaultCalibrationName)) return false;
        return LoadCalibration(defaultCalibrationName);
    }

    private void BuildJointLookup(CapturySkeleton skeleton)
    {
        jointLookup.Clear();
        foreach (var joint in skeleton.joints)
        {
            jointLookup[joint.name] = joint.transform;
            if (enableDebugLogging) Debug.Log($"MotionTrackingManager: Added joint: {joint.name}");
        }

        if (enableDebugLogging) Debug.Log($"MotionTrackingManager: Built joint lookup with {jointLookup.Count} joints");
    }

    private IEnumerator CalibrateSystem()
    {
        if (enableDebugLogging) Debug.Log($"MotionTrackingManager: Starting calibration in {config.calibrationDelay} seconds...");

        yield return new WaitForSeconds(config.calibrationDelay);

        if (enableDebugLogging) Debug.Log($"MotionTrackingManager: Calibrating {allModules.Count} modules...");

        foreach (var module in allModules)
        {
            if (module.HasRequiredJoints())
            {
                if (enableDebugLogging) Debug.Log($"MotionTrackingManager: Calibrating {module.GetType().Name}...");
                module.Calibrate();
            }
            else
            {
                Debug.LogWarning($"MotionTrackingManager: {module.GetType().Name} missing required joints!");
            }
        }

        isSystemCalibrated = true;
        activeCalibrationCoroutine = null;
        if (enableDebugLogging) Debug.Log("MotionTrackingManager: System calibrated and ready!");
    }

    private void CleanupModules()
    {
        if (enableDebugLogging) Debug.Log("MotionTrackingManager: Cleaning up modules...");

        foreach (var module in allModules)
        {
            if (module != null && module.gameObject != null)
            {
                if (enableDebugLogging) Debug.Log($"MotionTrackingManager: Destroying {module.GetType().Name}");
                // destroy the module parent (TrackingModules container)
                DestroyImmediate(module.gameObject.transform.parent.gameObject);
                break; // parent holds all modules, one destroy cleans them all
            }
        }
        allModules.Clear();
    }

    private void CleanupSystem()
    {
        if (networkPlugin != null)
            networkPlugin.SkeletonFound -= OnSkeletonFound;

        if (capturyInput != null && !dontDestroyOnLoad)
            InputSystem.RemoveDevice(capturyInput);

        CleanupModules();
    }

    #endregion

    #region Tracking Updates

    private void UpdateAllModules()
    {
        CapturyInputState state = new CapturyInputState();

        foreach (var module in allModules)
        {
            module.UpdateTracking(ref state);
        }

        InputSystem.QueueStateEvent(capturyInput, state);
    }

    #endregion

    #region Public Methods

    public Transform GetJointByName(string jointName)
    {
        jointLookup.TryGetValue(jointName, out Transform joint);
        return joint;
    }

    public void Recalibrate()
    {
        if (enableDebugLogging) Debug.Log("MotionTrackingManager: Manual recalibration requested");

        if (activeCalibrationCoroutine != null)
        {
            StopCoroutine(activeCalibrationCoroutine);
            activeCalibrationCoroutine = null;
        }

        isSystemCalibrated = false;
        activeCalibrationCoroutine = StartCoroutine(CalibrateSystem());
    }

    // revert all modules to their previous calibration snapshot.
    public bool LoadPreviousCalibration()
    {
        bool allRestored = true;
        foreach (var module in allModules)
        {
            if (!module.LoadPreviousCalibration())
            {
                allRestored = false;
                Debug.LogWarning($"MotionTrackingManager: {module.GetType().Name} had no previous calibration");
            }
        }

        if (enableDebugLogging)
            Debug.Log($"MotionTrackingManager: LoadPreviousCalibration — all restored: {allRestored}");

        return allRestored;
    }

    public void SwapConfiguration(MotionTrackingConfiguration newConfig)
    {
        if (newConfig == null)
        {
            Debug.LogError("MotionTrackingManager: Cannot swap to null configuration!");
            return;
        }

        if (enableDebugLogging)
            Debug.Log($"MotionTrackingManager: Swapping configuration from '{config?.configurationName ?? "none"}' to '{newConfig.configurationName}'");

        if (activeCalibrationCoroutine != null)
        {
            StopCoroutine(activeCalibrationCoroutine);
            activeCalibrationCoroutine = null;
        }

        bool wasCalibrated = isSystemCalibrated;
        isSystemCalibrated = false;

        CleanupModules();

        config = newConfig;

        if (enableDebugLogging)
            Debug.Log($"MotionTrackingManager: Initializing new modules for configuration '{config.configurationName}'");

        InitializeModules();

        if (wasCalibrated && jointLookup.Count > 0)
        {
            if (enableDebugLogging)
                Debug.Log("MotionTrackingManager: System was previously calibrated, recalibrating with new configuration...");
            activeCalibrationCoroutine = StartCoroutine(CalibrateSystem());
        }
        else if (jointLookup.Count == 0)
        {
            Debug.LogWarning("MotionTrackingManager: No joints available for calibration. Configuration swapped but not calibrated.");
        }

        if (enableDebugLogging)
            Debug.Log($"MotionTrackingManager: Configuration swap complete — '{config.configurationName}' with {allModules.Count} active modules");
    }

    // generic module access — works for any module type including custom ones
    public T GetModule<T>() where T : MotionTrackingModule
    {
        return allModules.OfType<T>().FirstOrDefault();
    }

    // public getters for built-in modules
    public TorsoTrackingModule GetTorsoModule() => GetModule<TorsoTrackingModule>();
    public FootTrackingModule GetFootModule() => GetModule<FootTrackingModule>();
    public ArmTrackingModule GetArmsModule() => GetModule<ArmTrackingModule>();
    public HeadTrackingModule GetHeadModule() => GetModule<HeadTrackingModule>();
    public BalanceTrackingModule GetBalanceModule() => GetModule<BalanceTrackingModule>();

    // module state checks — now config-driven
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

    // system state
    public bool IsSystemCalibrated => isSystemCalibrated;
    public bool IsCalibrating => activeCalibrationCoroutine != null;
    public int ActiveModuleCount => allModules.Count;
    public string CurrentConfigurationName => config?.configurationName ?? "None";

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
            Debug.LogWarning($"MotionTrackingManager: No saved calibration '{calibrationName}'");
            return false;
        }

        if (bundle.source != Source.ToString())
        {
            Debug.LogWarning($"MotionTrackingManager: Calibration '{calibrationName}' was captured on " +
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
}