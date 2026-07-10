using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;
using C3dWriter = Vub.Etro.IO.C3dWriter;
using C3dPoint = Vub.Etro.IO.Vector4;

[System.Serializable]
public class JointData
{
    public string name;
    public Vector3 position;
    public Quaternion rotation;
}

[System.Serializable]
public class MotionFrame
{
    public float timestamp;
    public JointData[] joints;
    public CapturyInputState inputState;

    public MotionFrame()
    {
        joints = new JointData[0];
    }
}

[System.Serializable]
public class MotionRecording
{
    public string sessionId;
    public string startTime;
    public string endTime;
    public float frameRate;
    public MotionFrame[] frames;
    public string[] metadataKeys;
    public string[] metadataValues;

    public MotionRecording()
    {
        frames = new MotionFrame[0];
        sessionId = System.Guid.NewGuid().ToString();
        startTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    }
}

/// <summary>
/// Records motion capture data to JSON (full fidelity, including biomechanical input state) and
/// natively writes a .c3d file (positions only) via the vendored c3d4sharp writer — no external
/// Python dependency. Source-agnostic: reads joints through whichever IMotionTrackingManager is
/// active in the scene (Captury, MediaPipe, Kinect, ...) rather than depending on one system.
/// </summary>
public class MotionRecorder : MonoBehaviour
{
    [Header("Recording Settings")]
    [SerializeField] private bool autoStartRecording = false;
    [SerializeField] private float recordingFrameRate = 30f;
    [SerializeField] private string outputFolderName = "MotionRecordings";
    [SerializeField] private bool recordInputStates = true;

    [Header("Debug")]
    [SerializeField] private bool debugMode = true;

    // recording state
    private bool isRecording = false;
    private List<MotionFrame> recordedFrames;
    private Dictionary<string, string> recordingMetadata;
    private float lastFrameTime;
    private float frameInterval;
    private string sessionTag = "";
    private string outputDirectory;

    private volatile string lastConversionStatus = "";
    private volatile string lastC3DPath = "";

    // active tracking source (Captury, MediaPipe, Kinect, ...) — resolved dynamically
    private IMotionTrackingManager trackingManager;

    public bool IsRecording => isRecording;
    public int FrameCount => recordedFrames?.Count ?? 0;
    public float RecordingDuration => isRecording ? Time.time - lastFrameTime : 0f;
    public bool HasTrackingSource => trackingManager != null;
    public string LastConversionStatus => lastConversionStatus;
    public string LastC3DPath => lastC3DPath;

    private void Start()
    {
        frameInterval = 1f / recordingFrameRate;
        outputDirectory = Path.Combine(Application.persistentDataPath, outputFolderName);

        if (!Directory.Exists(outputDirectory))
            Directory.CreateDirectory(outputDirectory);

        SceneManager.sceneLoaded += OnSceneLoaded;
        TryResolveTrackingManager();

        if (autoStartRecording)
            Invoke(nameof(StartRecording), 3f);
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;

        if (isRecording)
            StopRecording();
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        TryResolveTrackingManager();
    }

    /// <summary>
    /// Resolves the active tracking source. Prefers MotionTrackingOrchestrator — the toolkit's
    /// documented single entry point (Runtime/Scripts/Core/MotionTrackingOrchestrator.cs), which
    /// activates exactly one child manager and deactivates the others — over a raw scene scan,
    /// which could otherwise bind directly to a disabled-but-still-findable child manager
    /// instead of the orchestrator facade. Falls back to scanning for any IMotionTrackingManager
    /// for scenes that don't use the orchestrator prefab. Safe to call repeatedly.
    /// </summary>
    private void TryResolveTrackingManager()
    {
        if (MotionTrackingOrchestrator.Instance != null)
        {
            trackingManager = MotionTrackingOrchestrator.Instance;
            if (debugMode) Debug.Log($"MotionRecorder: Using tracking source '{trackingManager.Source}' (via MotionTrackingOrchestrator).");
            return;
        }

        foreach (var behaviour in FindObjectsOfType<MonoBehaviour>())
        {
            if (behaviour is IMotionTrackingManager manager)
            {
                trackingManager = manager;
                if (debugMode) Debug.Log($"MotionRecorder: Using tracking source '{manager.Source}' ({behaviour.GetType().Name}).");
                return;
            }
        }

        if (trackingManager == null && debugMode)
            Debug.LogWarning("MotionRecorder: No IMotionTrackingManager found in scene yet.");
    }

    /// <summary>
    /// Sets a tag (e.g. "{participantID}_{sessionNumber}") used to name the next recording file.
    /// </summary>
    public void SetSessionTag(string tag)
    {
        sessionTag = tag ?? "";
    }

    private void Update()
    {
        if (isRecording && trackingManager != null)
        {
            if (Time.time - lastFrameTime >= frameInterval)
            {
                RecordFrame();
                lastFrameTime = Time.time;
            }
        }
    }

    public void StartRecording()
    {
        if (isRecording)
        {
            Debug.LogWarning("Recording is already in progress!");
            return;
        }

        if (trackingManager == null)
            TryResolveTrackingManager();

        if (trackingManager == null)
        {
            Debug.LogWarning("MotionRecorder: No tracking source active! Cannot start recording.");
            return;
        }

        recordedFrames = new List<MotionFrame>();
        recordingMetadata = new Dictionary<string, string>();

        string[] lookupNames = JointNameSets.GetLookupNames(trackingManager.Source);
        int mappedJointCount = 0;
        foreach (var lookupName in lookupNames)
            if (trackingManager.GetJointByName(lookupName) != null)
                mappedJointCount++;

        recordingMetadata["Unity Version"] = Application.unityVersion;
        recordingMetadata["Scene"] = SceneManager.GetActiveScene().name;
        recordingMetadata["TrackingSource"] = trackingManager.Source.ToString();
        recordingMetadata["MappedJointCount"] = mappedJointCount.ToString();

        isRecording = true;
        lastFrameTime = Time.time;
        lastConversionStatus = "";
        lastC3DPath = "";

        Debug.Log($"Started motion recording! Source: {trackingManager.Source}, mapped joints: {mappedJointCount}/{JointNameSets.CanonicalNames.Length}");
    }

    public void StopRecording()
    {
        if (!isRecording)
        {
            Debug.LogWarning("No recording in progress!");
            return;
        }

        isRecording = false;

        SaveRecording();

        Debug.Log($"Stopped motion recording. Recorded {recordedFrames.Count} frames.");
    }

    private void RecordFrame()
    {
        var frame = new MotionFrame();
        frame.timestamp = Time.time;

        var jointDataList = new List<JointData>();
        string[] lookupNames = JointNameSets.GetLookupNames(trackingManager.Source);

        for (int i = 0; i < JointNameSets.CanonicalNames.Length; i++)
        {
            Transform joint = trackingManager.GetJointByName(lookupNames[i]);
            if (joint == null) continue;

            jointDataList.Add(new JointData
            {
                name = JointNameSets.CanonicalNames[i],
                position = joint.position,
                rotation = joint.rotation
            });
        }

        frame.joints = jointDataList.ToArray();

        if (recordInputStates)
            frame.inputState = GetCurrentInputState();

        recordedFrames.Add(frame);

        if (debugMode && recordedFrames.Count % 30 == 0)
            Debug.Log($"MotionRecorder: Recorded frame {recordedFrames.Count}, joints: {frame.joints.Length}");
    }

    /// <summary>
    /// Reads the current biomechanical state from the live CapturyInput device — populated every
    /// frame by whichever tracking manager is active, so this is already source-agnostic.
    /// </summary>
    private CapturyInputState GetCurrentInputState()
    {
        var inputState = new CapturyInputState();

        var device = InputSystem.GetDevice<CapturyInput>();
        if (device == null) return inputState;

        inputState.playerIndex = device.playerIndex.ReadValue();

        inputState.isBentOver = device.isBentOver.ReadValue();
        inputState.isUpright = device.isUpright.ReadValue();
        inputState.weightShiftLeft = device.weightShiftLeft.ReadValue();
        inputState.weightShiftRight = device.weightShiftRight.ReadValue();
        inputState.weightShiftX = device.weightShiftX.ReadValue();
        inputState.pelvisPosition = device.pelvisPosition.ReadValue();
        inputState.squatDepth = device.squatDepth.ReadValue();
        inputState.isSquatting = device.isSquatting.ReadValue();

        inputState.footRaised = device.footRaised.ReadValue();
        inputState.footLowered = device.footLowered.ReadValue();
        inputState.leftFootPosition = device.leftFootPosition.ReadValue();
        inputState.rightFootPosition = device.rightFootPosition.ReadValue();
        inputState.leftHipAbducted = device.leftHipAbducted.ReadValue();
        inputState.rightHipAbducted = device.rightHipAbducted.ReadValue();

        inputState.isWalking = device.isWalking.ReadValue();
        inputState.walkStarted = device.walkStarted.ReadValue();
        inputState.walkStopped = device.walkStopped.ReadValue();
        inputState.walkSpeed = device.walkSpeed.ReadValue();
        inputState.cadence = device.cadence.ReadValue();

        inputState.leftStep = device.leftStep.ReadValue();
        inputState.rightStep = device.rightStep.ReadValue();
        inputState.leftStepTime = device.leftStepTime.ReadValue();
        inputState.rightStepTime = device.rightStepTime.ReadValue();
        inputState.stepTimeAsymmetry = device.stepTimeAsymmetry.ReadValue();
        inputState.gaitConsistency = device.gaitConsistency.ReadValue();

        inputState.leftHandPosition = device.leftHandPosition.ReadValue();
        inputState.rightHandPosition = device.rightHandPosition.ReadValue();
        inputState.leftHandRaised = device.leftHandRaised.ReadValue();
        inputState.rightHandRaised = device.rightHandRaised.ReadValue();

        inputState.headPosition = device.headPosition.ReadValue();
        inputState.headRotation = device.headRotation.ReadValue();
        inputState.headUp = device.headUp.ReadValue();
        inputState.headDown = device.headDown.ReadValue();
        inputState.headLeft = device.headLeft.ReadValue();
        inputState.headRight = device.headRight.ReadValue();

        inputState.centerOfMassPosition = device.centerOfMassPosition.ReadValue();
        inputState.lateralSway = device.lateralSway.ReadValue();
        inputState.anteriorPosteriorSway = device.anteriorPosteriorSway.ReadValue();
        inputState.swayMagnitude = device.swayMagnitude.ReadValue();
        inputState.isSwaying = device.isSwaying.ReadValue();
        inputState.comVelocity = device.comVelocity.ReadValue();
        inputState.isBalanced = device.isBalanced.ReadValue();
        inputState.balanceLost = device.balanceLost.ReadValue();
        inputState.balanceRegained = device.balanceRegained.ReadValue();

        return inputState;
    }

    private void SaveRecording()
    {
        try
        {
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string tagPart = string.IsNullOrEmpty(sessionTag) ? "" : $"{sessionTag}_";
            string filename = $"motion_recording_{tagPart}{timestamp}.json";
            string filepath = Path.Combine(outputDirectory, filename);

            var recording = new MotionRecording();
            recording.frameRate = recordingFrameRate;
            recording.frames = recordedFrames.ToArray();
            recording.endTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            recording.metadataKeys = new string[recordingMetadata.Count];
            recording.metadataValues = new string[recordingMetadata.Count];
            int i = 0;
            foreach (var kvp in recordingMetadata)
            {
                recording.metadataKeys[i] = kvp.Key;
                recording.metadataValues[i] = kvp.Value;
                i++;
            }

            string json = JsonUtility.ToJson(recording, true);
            File.WriteAllText(filepath, json);

            Debug.Log($"Motion recording saved to: {filepath}");

            WriteC3D(filepath);
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to save recording: {e.Message}");
            lastConversionStatus = $"Save failed: {e.Message}";
        }
    }

    /// <summary>
    /// Writes a .c3d file (positions only — the JSON above stays the full-fidelity record) using
    /// the vendored c3d4sharp writer. Same coordinate convention the old Python script used:
    /// millimeters, X = right, Y = forward, Z = up, residual -1 for a joint missing that frame.
    /// </summary>
    private void WriteC3D(string jsonFilePath)
    {
        string c3dPath = Path.ChangeExtension(jsonFilePath, ".c3d");

        try
        {
            var writer = new C3dWriter(JointNameSets.CanonicalNames, recordingFrameRate, new string[0], 0, false);

            // c3d4sharp defaults POINT:SCALE to a positive value (= 16-bit integer storage),
            // but WriteFloatFrame writes 4-byte floats — must flag float storage explicitly,
            // or compliant readers will misinterpret the data section. See Readme.txt.
            writer.Header.ScaleFactor = -1f;
            writer.SetParameter<float>("POINT:SCALE", -1f);

            writer.Open(c3dPath);

            foreach (var frame in recordedFrames)
            {
                // Recenter on hips so the recording sits near the origin regardless of where the
                // person stood relative to the tracking rig's world origin that session. The JSON
                // above keeps raw world coordinates — this offset is applied only for the C3D.
                JointData hips = Array.Find(frame.joints, j => j.name == "SpineBase");
                Vector3 origin = hips != null ? hips.position : Vector3.zero;

                var points = new C3dPoint[JointNameSets.CanonicalNames.Length];
                for (int i = 0; i < JointNameSets.CanonicalNames.Length; i++)
                {
                    JointData joint = Array.Find(frame.joints, j => j.name == JointNameSets.CanonicalNames[i]);
                    points[i] = joint != null
                        ? new C3dPoint((joint.position.x - origin.x) * 1000f, (joint.position.z - origin.z) * 1000f, (joint.position.y - origin.y) * 1000f, 0f)
                        : new C3dPoint(0f, 0f, 0f, -1f);
                }
                writer.WriteFloatFrame(points);
            }

            writer.Close();

            lastC3DPath = c3dPath;
            lastConversionStatus = $"C3D written: {c3dPath}";
            Debug.Log($"MotionRecorder: {lastConversionStatus}");
        }
        catch (Exception e)
        {
            lastConversionStatus = $"C3D write failed: {e.Message}";
            Debug.LogError($"MotionRecorder: {lastConversionStatus}");
        }
    }
}
