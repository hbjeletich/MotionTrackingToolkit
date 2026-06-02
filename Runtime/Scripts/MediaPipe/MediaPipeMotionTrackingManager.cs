using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

/// <summary>
/// IMotionTrackingManager for MediaPipe webcam input via Python UDP.
///
/// Reads 3D landmark positions from MediaPipeInput, maps them to named joint
/// Transforms, computes virtual joints (Hips, Neck, Spine, etc.), and drives
/// the same module system as MotionTrackingManager.
///
/// Assign the same MotionTrackingConfiguration asset you use for Captury/Kinect.
/// All existing modules work unchanged.
///
/// Scene setup:
///   GameObject with MediaPipeInput + MediaPipeMotionTrackingManager
///   Run mediapipe_sender.py alongside Unity
/// </summary>
public class MediaPipeMotionTrackingManager : MonoBehaviour, IMotionTrackingManager
{
    #region Configuration

    [Header("Configuration")]
    [SerializeField] private MotionTrackingConfiguration config;
    [SerializeField] private bool dontDestroyOnLoad = true;
    [SerializeField] private bool enableDebugLogging = true;

    [Header("References")]
    [SerializeField] private MediaPipeInput mediaPipeInput;

    [Header("Coordinate Mapping")]
    [Tooltip("Scale applied to landmark positions. Default 1.0 = meters (MediaPipe world landmark scale).")]
    [SerializeField] private float positionScale = 1.0f;

    [Tooltip("World offset applied to all joints (positions the skeleton in the scene)")]
    [SerializeField] private Vector3 worldOffset = Vector3.zero;

    #endregion

    #region MediaPipe Landmark Indices

    public enum PoseLandmark
    {
        Nose = 0,
        LeftEyeInner = 1, LeftEye = 2, LeftEyeOuter = 3,
        RightEyeInner = 4, RightEye = 5, RightEyeOuter = 6,
        LeftEar = 7, RightEar = 8,
        MouthLeft = 9, MouthRight = 10,
        LeftShoulder = 11, RightShoulder = 12,
        LeftElbow = 13, RightElbow = 14,
        LeftWrist = 15, RightWrist = 16,
        LeftPinky = 17, RightPinky = 18,
        LeftIndex = 19, RightIndex = 20,
        LeftThumb = 21, RightThumb = 22,
        LeftHip = 23, RightHip = 24,
        LeftKnee = 25, RightKnee = 26,
        LeftAnkle = 27, RightAnkle = 28,
        LeftHeel = 29, RightHeel = 30,
        LeftFootIndex = 31, RightFootIndex = 32
    }

    #endregion

    #region Joint Mapping

    // maps module config joint names → direct MediaPipe landmark indices
    private static readonly Dictionary<string, PoseLandmark> DirectJointMapping = new Dictionary<string, PoseLandmark>
    {
        // arm module
        { "LeftHand",       PoseLandmark.LeftWrist },
        { "RightHand",      PoseLandmark.RightWrist },
        { "LeftShoulder",   PoseLandmark.LeftShoulder },
        { "RightShoulder",  PoseLandmark.RightShoulder },

        // foot module
        { "LeftFoot",       PoseLandmark.LeftAnkle },
        { "RightFoot",      PoseLandmark.RightAnkle },

        // balance module
        { "LeftForeArm",    PoseLandmark.LeftElbow },
        { "RightForeArm",   PoseLandmark.RightElbow },
        { "LeftLeg",        PoseLandmark.LeftKnee },
        { "RightLeg",       PoseLandmark.RightKnee },
        { "LeftToeBase",    PoseLandmark.LeftFootIndex },
        { "RightToeBase",   PoseLandmark.RightFootIndex },
    };

    #endregion

    #region Private State

    // skeleton transforms
    private Transform[] landmarkTransforms = new Transform[33];
    private GameObject skeletonRoot;

    // virtual joints (computed from landmark positions)
    private Transform headJoint;
    private Transform neckJoint;
    private Transform hipsJoint;
    private Transform spineJoint;
    private Transform spine1Joint;
    private Transform spine4Joint;

    // joint lookup for IMotionTrackingManager
    private Dictionary<string, Transform> jointLookup = new Dictionary<string, Transform>();

    // buffers for reading from MediaPipeInput
    private Vector3[] positionBuffer = new Vector3[33];
    private float[] confidenceBuffer = new float[33];

    // modules and input device
    private List<MotionTrackingModule> allModules = new List<MotionTrackingModule>();
    private CapturyInput capturyInput;

    // state
    private bool isSystemCalibrated = false;
    private Coroutine activeCalibrationCoroutine = null;
    private bool hasReceivedLandmarks = false;

    // singleton
    private static MediaPipeMotionTrackingManager instance;
    public static MediaPipeMotionTrackingManager Instance => instance;

    #endregion

    #region IMotionTrackingManager

    public MotionTrackingConfiguration Config => config;
    public MotionSource Source => MotionSource.MediaPipe;

    public Transform GetJointByName(string jointName)
    {
        jointLookup.TryGetValue(jointName, out Transform joint);
        return joint;
    }

    #endregion

    #region Public State

    public bool IsSystemCalibrated => isSystemCalibrated;
    public bool IsCalibrating => activeCalibrationCoroutine != null;
    public bool HasReceivedLandmarks => hasReceivedLandmarks;
    public int ActiveModuleCount => allModules.Count;
    public string CurrentConfigurationName => config?.configurationName ?? "None";

    #endregion

    #region Unity Lifecycle

    void Awake()
    {
        SetupSingleton();

        if (config == null)
        {
            Debug.LogWarning("MediaPipeMotionTrackingManager: No configuration assigned, creating default");
            config = ScriptableObject.CreateInstance<MotionTrackingConfiguration>();
        }

        CreateSkeletonTransforms();
        BuildJointLookup();
        InitializeCapturyInput();
        InitializeModules();
    }

    void Start()
    {
        if (mediaPipeInput == null)
            mediaPipeInput = GetComponent<MediaPipeInput>();

        if (mediaPipeInput == null)
        {
            Debug.LogError("MediaPipeMotionTrackingManager: No MediaPipeInput found! Add one to this GameObject.");
            return;
        }

        if (enableDebugLogging)
            Debug.Log($"MediaPipeMotionTrackingManager: Ready with {allModules.Count} modules, waiting for landmarks...");
    }

    void Update()
    {
        if (mediaPipeInput == null || !mediaPipeInput.HasData) return;

        // read latest landmarks from the input receiver
        if (!mediaPipeInput.TryGetLandmarks(positionBuffer, confidenceBuffer))
            return;

        // update skeleton transforms
        UpdateLandmarkPositions();
        ComputeVirtualJoints();

        // first detection triggers calibration
        if (!hasReceivedLandmarks)
        {
            hasReceivedLandmarks = true;
            if (enableDebugLogging)
                Debug.Log("MediaPipeMotionTrackingManager: First landmarks received, calibrating...");
            activeCalibrationCoroutine = StartCoroutine(CalibrateSystem());
        }

        // drive modules
        if (isSystemCalibrated && capturyInput != null)
        {
            UpdateAllModules();
        }
    }

    void OnDestroy()
    {
        if (enableDebugLogging)
            Debug.Log("MediaPipeMotionTrackingManager: OnDestroy()");

        CleanupSystem();
    }

    #endregion

    #region Setup

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

    private void CreateSkeletonTransforms()
    {
        skeletonRoot = new GameObject("MediaPipeSkeleton");
        skeletonRoot.transform.SetParent(transform);

        // 33 raw landmark transforms
        string[] names = System.Enum.GetNames(typeof(PoseLandmark));
        for (int i = 0; i < 33; i++)
        {
            GameObject go = new GameObject($"MP_{names[i]}");
            go.transform.SetParent(skeletonRoot.transform);
            landmarkTransforms[i] = go.transform;
        }

        // virtual joints (midpoints and derived positions)
        headJoint = CreateChild("Virtual_Head");
        neckJoint = CreateChild("Virtual_Neck");
        hipsJoint = CreateChild("Virtual_Hips");
        spineJoint = CreateChild("Virtual_Spine");
        spine1Joint = CreateChild("Virtual_Spine1");
        spine4Joint = CreateChild("Virtual_Spine4");

        if (enableDebugLogging)
            Debug.Log("MediaPipeMotionTrackingManager: Skeleton transforms created");
    }

    private Transform CreateChild(string name)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(skeletonRoot.transform);
        return go.transform;
    }

    private void BuildJointLookup()
    {
        jointLookup.Clear();

        // direct landmark mappings
        foreach (var kvp in DirectJointMapping)
            jointLookup[kvp.Key] = landmarkTransforms[(int)kvp.Value];

        // virtual joints override where needed
        jointLookup["Head"] = headJoint;
        jointLookup["Neck"] = neckJoint;
        jointLookup["Hips"] = hipsJoint;
        jointLookup["Spine"] = spineJoint;
        jointLookup["Spine1"] = spine1Joint;
        jointLookup["Spine4"] = spine4Joint;

        if (enableDebugLogging)
            Debug.Log($"MediaPipeMotionTrackingManager: Joint lookup built with {jointLookup.Count} entries");
    }

    private void InitializeCapturyInput()
    {
        capturyInput = InputSystem.GetDevice<CapturyInput>();
        if (capturyInput == null)
        {
            CapturyInput.Register();
            capturyInput = InputSystem.AddDevice<CapturyInput>();
        }

        if (enableDebugLogging)
            Debug.Log("MediaPipeMotionTrackingManager: CapturyInput device ready");
    }

    private void InitializeModules()
    {
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
                    Debug.Log($"MediaPipeMotionTrackingManager: Initialized {module.GetType().Name}");
            }
        }
    }

    #endregion

    #region Landmark Updates

    private void UpdateLandmarkPositions()
    {
        for (int i = 0; i < 33; i++)
        {
            landmarkTransforms[i].localPosition = positionBuffer[i] * positionScale + worldOffset;
        }
    }

    private void ComputeVirtualJoints()
    {
        Vector3 leftHip = landmarkTransforms[(int)PoseLandmark.LeftHip].localPosition;
        Vector3 rightHip = landmarkTransforms[(int)PoseLandmark.RightHip].localPosition;
        Vector3 leftShoulder = landmarkTransforms[(int)PoseLandmark.LeftShoulder].localPosition;
        Vector3 rightShoulder = landmarkTransforms[(int)PoseLandmark.RightShoulder].localPosition;
        Vector3 nose = landmarkTransforms[(int)PoseLandmark.Nose].localPosition;
        Vector3 leftEar = landmarkTransforms[(int)PoseLandmark.LeftEar].localPosition;
        Vector3 rightEar = landmarkTransforms[(int)PoseLandmark.RightEar].localPosition;

        // ── Positions ──

        // Hips = midpoint of left/right hip
        Vector3 hips = (leftHip + rightHip) * 0.5f;
        hipsJoint.localPosition = hips;

        // Spine4 (upper chest) = midpoint of shoulders
        Vector3 upperChest = (leftShoulder + rightShoulder) * 0.5f;
        spine4Joint.localPosition = upperChest;

        // Spine = 25% up from hips to shoulders (lower spine)
        spineJoint.localPosition = Vector3.Lerp(hips, upperChest, 0.25f);

        // Spine1 = 50% up from hips to shoulders (mid-torso, used by Balance module)
        spine1Joint.localPosition = Vector3.Lerp(hips, upperChest, 0.5f);

        // Neck = midpoint between upper chest and nose
        neckJoint.localPosition = Vector3.Lerp(upperChest, nose, 0.5f);

        // Head = nose position
        headJoint.localPosition = nose;

        // ── Rotations ──

        // Hips rotation (TorsoModule uses this for bend detection)
        Vector3 spineDir = (upperChest - hips).normalized;
        if (spineDir.sqrMagnitude > 0.001f)
        {
            Vector3 hipRight = (rightHip - leftHip).normalized;
            Vector3 hipForward = Vector3.Cross(hipRight, spineDir).normalized;
            if (hipForward.sqrMagnitude > 0.001f)
                hipsJoint.localRotation = Quaternion.LookRotation(hipForward, spineDir);
        }

        // Head rotation (HeadModule uses this for direction detection)
        Vector3 headForward = (nose - neckJoint.localPosition).normalized;
        Vector3 headRight = (rightEar - leftEar).normalized;
        if (headForward.sqrMagnitude > 0.001f && headRight.sqrMagnitude > 0.001f)
        {
            Vector3 headUp = Vector3.Cross(headForward, headRight).normalized;
            headJoint.localRotation = Quaternion.LookRotation(headForward, headUp);
            neckJoint.localRotation = Quaternion.LookRotation(headForward, Vector3.up);
        }
    }

    #endregion

    #region Calibration

    private IEnumerator CalibrateSystem()
    {
        if (enableDebugLogging)
            Debug.Log($"MediaPipeMotionTrackingManager: Calibrating in {config.calibrationDelay}s...");

        yield return new WaitForSeconds(config.calibrationDelay);

        if (enableDebugLogging)
            Debug.Log($"MediaPipeMotionTrackingManager: Calibrating {allModules.Count} modules...");

        foreach (var module in allModules)
        {
            if (module.HasRequiredJoints())
            {
                module.Calibrate();
                if (enableDebugLogging)
                    Debug.Log($"MediaPipeMotionTrackingManager: Calibrated {module.GetType().Name}");
            }
            else
            {
                Debug.LogWarning($"MediaPipeMotionTrackingManager: {module.GetType().Name} missing required joints!");
                LogMissingJoints(module);
            }
        }

        isSystemCalibrated = true;
        activeCalibrationCoroutine = null;

        if (enableDebugLogging)
            Debug.Log("MediaPipeMotionTrackingManager: Calibration complete!");
    }

    private void LogMissingJoints(MotionTrackingModule module)
    {
        foreach (string name in module.GetRequiredJointNames())
        {
            if (GetJointByName(name) == null)
                Debug.LogWarning($"  → Missing joint: '{name}'");
        }
    }

    #endregion

    #region Module Updates

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

    #region Cleanup

    private void CleanupModules()
    {
        foreach (var module in allModules)
        {
            if (module != null && module.gameObject != null)
            {
                DestroyImmediate(module.gameObject.transform.parent.gameObject);
                break; // parent holds all modules
            }
        }
        allModules.Clear();
    }

    private void CleanupSystem()
    {
        if (activeCalibrationCoroutine != null)
            StopCoroutine(activeCalibrationCoroutine);

        CleanupModules();

        if (capturyInput != null && !dontDestroyOnLoad)
            InputSystem.RemoveDevice(capturyInput);
    }

    #endregion

    #region Public API

    public void Recalibrate()
    {
        if (enableDebugLogging)
            Debug.Log("MediaPipeMotionTrackingManager: Manual recalibration requested");

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
                Debug.LogWarning($"MediaPipeMotionTrackingManager: {module.GetType().Name} had no previous calibration");
            }
        }

        if (enableDebugLogging)
            Debug.Log($"MediaPipeMotionTrackingManager: LoadPreviousCalibration — all restored: {allRestored}");

        return allRestored;
    }

    public void SwapConfiguration(MotionTrackingConfiguration newConfig)
    {
        if (newConfig == null)
        {
            Debug.LogError("MediaPipeMotionTrackingManager: Cannot swap to null configuration!");
            return;
        }

        if (enableDebugLogging)
            Debug.Log($"MediaPipeMotionTrackingManager: Swapping to '{newConfig.configurationName}'");

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

        if (wasCalibrated && hasReceivedLandmarks)
        {
            activeCalibrationCoroutine = StartCoroutine(CalibrateSystem());
        }

        if (enableDebugLogging)
            Debug.Log($"MediaPipeMotionTrackingManager: Swap complete — {allModules.Count} modules");
    }

    /// <summary>
    /// Add a custom joint name → landmark mapping at runtime.
    /// Call before calibration if your configs use non-default joint names.
    /// </summary>
    public void AddJointMapping(string jointName, PoseLandmark landmark)
    {
        jointLookup[jointName] = landmarkTransforms[(int)landmark];
    }

    // generic module access
    public T GetModule<T>() where T : MotionTrackingModule
    {
        return allModules.OfType<T>().FirstOrDefault();
    }

    // convenience getters matching MotionTrackingManager's API
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

    /// <summary>
    /// Get a raw landmark Transform by index (0-32).
    /// </summary>
    public Transform GetLandmarkTransform(int index)
    {
        if (index >= 0 && index < 33) return landmarkTransforms[index];
        return null;
    }

    public Transform GetLandmarkTransform(PoseLandmark landmark)
    {
        return landmarkTransforms[(int)landmark];
    }

    /// <summary>
    /// Get the confidence value for a landmark (0-1).
    /// </summary>
    public float GetLandmarkConfidence(int index)
    {
        if (index >= 0 && index < 33) return confidenceBuffer[index];
        return 0f;
    }

    #endregion
}
