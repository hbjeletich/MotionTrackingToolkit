using UnityEngine;
using UnityEngine.InputSystem.LowLevel;
using System.Collections.Generic;
using System.Linq;

public class FootTrackingModule : MotionTrackingModule
{
    #region Data Structures

    [System.Serializable]
    public class FootCalibrationSnapshot : CalibrationSnapshot
    {
        public Vector3 neutralLeftFootPosition;
        public Vector3 neutralRightFootPosition;
        public float defaultFootDistance;
        public float groundHeight;

        public override CalibrationSnapshot Clone()
        {
            return new FootCalibrationSnapshot
            {
                timestamp = timestamp,
                neutralLeftFootPosition = neutralLeftFootPosition,
                neutralRightFootPosition = neutralRightFootPosition,
                defaultFootDistance = defaultFootDistance,
                groundHeight = groundHeight
            };
        }
    }

    [System.Serializable]
    public struct FootEvent
    {
        public float timestamp;
        public bool isLeftFoot;
        public bool isFootDown;
        public Vector3 position;

        public FootEvent(float time, bool left, bool down, Vector3 pos)
        {
            timestamp = time;
            isLeftFoot = left;
            isFootDown = down;
            position = pos;
        }
    }

    public enum WalkState
    {
        Idle,
        InitiatingWalk,
        Walking,
        Stopping
    }

    #endregion

    #region Variables

    // internal states
    private bool isFootRaised = false;
    private bool isLeftHipAbducted = false;
    private bool isRightHipAbducted = false;

    // walk tracking
    private WalkState currentWalkState = WalkState.Idle;
    private WalkState previousWalkState = WalkState.Idle;
    private float walkStateChangeTime = 0f;
    private float currentWalkSpeed = 0f;

    // data buffers
    private Queue<Vector3> spinePositionHistory;
    private Queue<float> timestampHistory;
    private Queue<FootEvent> footEventHistory;

    // foot contact detection
    private bool leftFootInContact = false;
    private bool rightFootInContact = false;

    // gait analysis
    private float lastLeftStepTime = 0f;
    private float lastRightStepTime = 0f;
    private float lastLeftContactTime = 0f;
    private float lastRightContactTime = 0f;
    private float currentCadence = 0f;
    private List<float> recentStepTimes;

    // calibration access
    private FootModuleConfiguration FootConfig => GetModuleConfig() as FootModuleConfiguration;
    private FootCalibrationSnapshot FootCalibration => CurrentCalibration as FootCalibrationSnapshot;

    // config values with fallbacks
    public bool IsFootRaiseTracked => FootConfig?.isFootRaiseTracked ?? true;
    public bool IsHipAbductionTracked => FootConfig?.isHipAbductionTracked ?? true;
    public bool IsFootPositionTracked => FootConfig?.isFootPositionTracked ?? true;
    public bool UseRelativeFootPosition => FootConfig?.useRelativeFootPosition ?? true;
    public float FootRaiseThreshold => FootConfig?.footRaiseThreshold ?? 0.1f;
    public float MinAbductionDistance => FootConfig?.minAbductionDistance ?? 0.2f;
    public float MinLiftHeight => FootConfig?.minLiftHeight ?? 0.05f;

    public bool IsWalkTrackingEnabled => FootConfig?.enableWalkTracking ?? false;
    public float WalkSpeedThreshold => FootConfig?.walkSpeedThreshold ?? 0.3f;
    public float MinimumWalkDuration => FootConfig?.minimumWalkDuration ?? 2.0f;
    public float WalkStopThreshold => FootConfig?.walkStopThreshold ?? 0.1f;

    public bool IsGaitAnalysisEnabled => FootConfig?.enableGaitAnalysis ?? false;
    public int MinimumCyclesForAnalysis => FootConfig?.minimumCyclesForAnalysis ?? 3;
    public float MaxReasonableStepTime => FootConfig?.maxReasonableStepTime ?? 2.0f;
    public float MinReasonableStepTime => FootConfig?.minReasonableStepTime ?? 0.3f;
    public int PositionHistoryFrames => FootConfig?.positionHistoryFrames ?? 300;
    public int EventHistoryCount => FootConfig?.eventHistoryCount ?? 20;

    #endregion

    #region Base Class Implementation

    public override ModuleConfiguration GetModuleConfig()
    {
        return manager?.Config?.GetModuleConfig<FootModuleConfiguration>();
    }

    public override void Initialize(IMotionTrackingManager manager)
    {
        base.Initialize(manager);

        spinePositionHistory = new Queue<Vector3>();
        timestampHistory = new Queue<float>();
        footEventHistory = new Queue<FootEvent>();
        recentStepTimes = new List<float>();

        Debug.Log($"FootTrackingModule: Initialized with Walk: {IsWalkTrackingEnabled}, Gait: {IsGaitAnalysisEnabled}");
    }

    protected override CalibrationSnapshot CaptureCalibration()
    {
        string leftFootName = FootConfig?.leftFootJointName ?? "LeftFoot";
        string rightFootName = FootConfig?.rightFootJointName ?? "RightFoot";

        Transform leftFoot = GetJoint(leftFootName);
        Transform rightFoot = GetJoint(rightFootName);

        if (leftFoot == null || rightFoot == null)
        {
            Debug.LogError("FootTrackingModule: Missing foot joints during calibration capture");
            return null;
        }

        Vector3 leftPos = leftFoot.position;
        Vector3 rightPos = rightFoot.position;

        Vector2 leftPos2D = new Vector2(leftPos.x, leftPos.z);
        Vector2 rightPos2D = new Vector2(rightPos.x, rightPos.z);

        var snapshot = new FootCalibrationSnapshot
        {
            neutralLeftFootPosition = leftPos,
            neutralRightFootPosition = rightPos,
            defaultFootDistance = Vector2.Distance(leftPos2D, rightPos2D),
            groundHeight = (leftPos.y + rightPos.y) / 2.0f
        };

        ClearHistoryBuffers();

        Debug.Log($"FootTrackingModule: Captured calibration — Ground: {snapshot.groundHeight:F3}, FootDist: {snapshot.defaultFootDistance:F3}");
        return snapshot;
    }

    protected override void OnCalibrationApplied()
    {
        isFootRaised = false;
        isLeftHipAbducted = false;
        isRightHipAbducted = false;
        ClearHistoryBuffers();
    }

    public override string SerializeCalibration()
    {
        var cal = CurrentCalibration as FootCalibrationSnapshot;
        if (cal == null) return null;
        return JsonUtility.ToJson(cal);
    }

    public override void DeserializeCalibration(string json)
    {
        if (string.IsNullOrEmpty(json)) return;
        var snapshot = JsonUtility.FromJson<FootCalibrationSnapshot>(json);
        if (snapshot != null) SetCalibration(snapshot);
    }

    #endregion

    #region Main Update Loop

    public override void UpdateTracking(ref CapturyInputState state)
    {
        if (!IsEnabled || !IsCalibrated) return;

        string leftFootName = FootConfig.leftFootJointName;
        string rightFootName = FootConfig.rightFootJointName;
        Transform leftFoot = GetJoint(leftFootName);
        Transform rightFoot = GetJoint(rightFootName);

        if (leftFoot == null || rightFoot == null)
        {
            if (DebugMode && Time.frameCount % 300 == 0)
                Debug.Log("FootTrackingModule: Missing tracked foot joints");
            return;
        }

        var cal = FootCalibration;
        Vector3 leftPos = leftFoot.position;
        Vector3 rightPos = rightFoot.position;

        // Prefer the room-calibrated floor plane over the flat groundHeight captured
        // during foot calibration — it stays accurate if the camera is tilted or
        // mounted at a different height than it was during calibration.
        var fp = GetFloorPlane();
        float leftHeight = fp.HasValue ? fp.Value.GetDistanceToPoint(leftPos) : leftPos.y - cal.groundHeight;
        float rightHeight = fp.HasValue ? fp.Value.GetDistanceToPoint(rightPos) : rightPos.y - cal.groundHeight;

        if (DebugMode && Time.frameCount % 120 == 0)
            Debug.Log($"FootTrackingModule: floorPlane={fp.HasValue} L={leftHeight:F3} R={rightHeight:F3}");

        // update data buffers
        if (IsWalkTrackingEnabled || IsGaitAnalysisEnabled)
            UpdateDataBuffers();

        // layer 1: basic foot tracking
        UpdateBasicFootTracking(ref state, leftPos, rightPos, leftHeight, rightHeight);

        // layer 2: walk detection
        if (IsWalkTrackingEnabled)
            UpdateWalkDetection(ref state);

        // layer 3: gait analysis
        if (IsGaitAnalysisEnabled)
            UpdateGaitAnalysis(ref state, leftHeight, rightHeight);

        // update walk state output
        if (IsWalkTrackingEnabled)
            UpdateWalkState(ref state);
    }

    #endregion

    #region Layer 1: Basic Foot Tracking

    private void UpdateBasicFootTracking(ref CapturyInputState state, Vector3 leftPos, Vector3 rightPos, float leftHeight, float rightHeight)
    {
        if (IsFootRaiseTracked)
            UpdateFootRaise(ref state, leftHeight, rightHeight);
        if (IsHipAbductionTracked)
            UpdateHipAbduction(ref state, leftPos, rightPos, leftHeight, rightHeight);
        if (IsFootPositionTracked)
            UpdateFootPositions(ref state, leftPos, rightPos);
    }

    private void UpdateFootRaise(ref CapturyInputState state, float leftHeight, float rightHeight)
    {
        float footHeightDifference = Mathf.Abs(leftHeight - rightHeight);
        bool footRaisedNow = footHeightDifference > FootRaiseThreshold;

        if (footRaisedNow && !isFootRaised)
        {
            isFootRaised = true;
            state.footRaised = 1.0f;
            state.footLowered = 0.0f;
            if (DebugMode) Debug.Log("FootTrackingModule: FOOT RAISED!");
        }
        else if (!footRaisedNow && isFootRaised)
        {
            isFootRaised = false;
            state.footRaised = 0.0f;
            state.footLowered = 1.0f;
            if (DebugMode) Debug.Log("FootTrackingModule: FOOT LOWERED!");
        }
        else
        {
            state.footRaised = isFootRaised ? 1.0f : 0.0f;
            state.footLowered = 0.0f;
        }
    }

    private void UpdateHipAbduction(ref CapturyInputState state, Vector3 leftPos, Vector3 rightPos, float leftHeight, float rightHeight)
    {
        var cal = FootCalibration;
        Vector2 leftPos2D = new Vector2(leftPos.x, leftPos.z);
        Vector2 rightPos2D = new Vector2(rightPos.x, rightPos.z);
        float currentDistance = Vector2.Distance(leftPos2D, rightPos2D);
        float abductionDistance = currentDistance - cal.defaultFootDistance;

        bool leftLiftedEnough = leftHeight > MinLiftHeight;
        bool rightLiftedEnough = rightHeight > MinLiftHeight;
        bool distanceIncreased = abductionDistance > MinAbductionDistance;

        bool leftAbductedNow = leftLiftedEnough && distanceIncreased;
        bool rightAbductedNow = rightLiftedEnough && distanceIncreased;

        if (leftAbductedNow != isLeftHipAbducted)
        {
            isLeftHipAbducted = leftAbductedNow;
            if (DebugMode) Debug.Log($"LEFT HIP ABDUCTION {(leftAbductedNow ? "ON" : "OFF")}");
        }
        state.leftHipAbducted = isLeftHipAbducted ? 1.0f : 0.0f;

        if (rightAbductedNow != isRightHipAbducted)
        {
            isRightHipAbducted = rightAbductedNow;
            if (DebugMode) Debug.Log($"RIGHT HIP ABDUCTION {(rightAbductedNow ? "ON" : "OFF")}");
        }
        state.rightHipAbducted = isRightHipAbducted ? 1.0f : 0.0f;
    }

    private void UpdateFootPositions(ref CapturyInputState state, Vector3 leftPos, Vector3 rightPos)
    {
        var cal = FootCalibration;
        if (UseRelativeFootPosition)
        {
            state.leftFootPosition = (leftPos - cal.neutralLeftFootPosition) * Sensitivity;
            state.rightFootPosition = (rightPos - cal.neutralRightFootPosition) * Sensitivity;
        }
        else
        {
            state.leftFootPosition = leftPos * Sensitivity;
            state.rightFootPosition = rightPos * Sensitivity;
        }
    }

    #endregion

    #region Layer 2: Walk Detection

    private void UpdateDataBuffers()
    {
        timestampHistory.Enqueue(Time.time);

        string spineName = FootConfig?.walkTrackingSpineJointName ?? "Spine";
        Transform spine = GetJoint(spineName);
        if (spine != null)
            spinePositionHistory.Enqueue(spine.position);

        while (spinePositionHistory.Count > PositionHistoryFrames)
        {
            spinePositionHistory.Dequeue();
            timestampHistory.Dequeue();
        }
    }

    private void UpdateWalkDetection(ref CapturyInputState state)
    {
        float currentSpeed = CalculateCurrentSpeed();
        previousWalkState = currentWalkState;

        switch (currentWalkState)
        {
            case WalkState.Idle:
                if (currentSpeed > WalkSpeedThreshold)
                {
                    currentWalkState = WalkState.InitiatingWalk;
                    walkStateChangeTime = Time.time;
                    if (DebugMode) Debug.Log("Walk: Idle -> InitiatingWalk");
                }
                break;

            case WalkState.InitiatingWalk:
                if (currentSpeed < WalkStopThreshold)
                {
                    currentWalkState = WalkState.Idle;
                    if (DebugMode) Debug.Log("Walk: InitiatingWalk -> Idle (false start)");
                }
                else if (Time.time - walkStateChangeTime > MinimumWalkDuration)
                {
                    currentWalkState = WalkState.Walking;
                    if (DebugMode) Debug.Log("Walk: InitiatingWalk -> Walking");
                }
                break;

            case WalkState.Walking:
                if (currentSpeed < WalkStopThreshold)
                {
                    currentWalkState = WalkState.Stopping;
                    walkStateChangeTime = Time.time;
                    if (DebugMode) Debug.Log("Walk: Walking -> Stopping");
                }
                break;

            case WalkState.Stopping:
                if (currentSpeed > WalkSpeedThreshold)
                {
                    currentWalkState = WalkState.Walking;
                    if (DebugMode) Debug.Log("Walk: Stopping -> Walking (resumed)");
                }
                else if (Time.time - walkStateChangeTime > 1.0f)
                {
                    currentWalkState = WalkState.Idle;
                    if (DebugMode) Debug.Log("Walk: Stopping -> Idle");
                }
                break;
        }

        currentWalkSpeed = currentSpeed;
    }

    private float CalculateCurrentSpeed()
    {
        if (spinePositionHistory.Count < 30) return 0f;

        Vector3[] recentPositions = spinePositionHistory.TakeLast(30).ToArray();
        if (recentPositions.Length < 2) return 0f;

        Vector3 movement = recentPositions[recentPositions.Length - 1] - recentPositions[0];
        float timeSpan = 0.5f; // 30 frames at 60fps
        return movement.magnitude / timeSpan;
    }

    #endregion

    #region Layer 3: Gait Analysis

    private void UpdateGaitAnalysis(ref CapturyInputState state, float leftHeight, float rightHeight)
    {
        float currentTime = Time.time;

        string leftFootName = FootConfig.leftFootJointName;
        string rightFootName = FootConfig.rightFootJointName;
        Transform leftFoot = GetJoint(leftFootName);
        Transform rightFoot = GetJoint(rightFootName);

        bool leftContactNow = leftHeight < (MinLiftHeight * 0.5f);
        bool rightContactNow = rightHeight < (MinLiftHeight * 0.5f);

        // left foot step detection
        if (leftContactNow && !leftFootInContact)
        {
            leftFootInContact = true;
            if (leftFoot != null) RecordFootEvent(currentTime, true, true, leftFoot.position);

            if (lastLeftContactTime > 0)
            {
                float stepTime = currentTime - lastLeftContactTime;
                if (stepTime >= MinReasonableStepTime && stepTime <= MaxReasonableStepTime)
                {
                    lastLeftStepTime = stepTime;
                    state.leftStep = 1.0f;
                    state.leftStepTime = stepTime;
                    recentStepTimes.Add(stepTime);
                    if (DebugMode) Debug.Log($"LEFT STEP: {stepTime:F3}s");
                }
            }
            lastLeftContactTime = currentTime;
        }
        else if (!leftContactNow && leftFootInContact)
        {
            leftFootInContact = false;
            if (leftFoot != null) RecordFootEvent(currentTime, true, false, leftFoot.position);
        }
        else
        {
            state.leftStep = 0.0f;
        }

        // right foot step detection
        if (rightContactNow && !rightFootInContact)
        {
            rightFootInContact = true;
            if (rightFoot != null) RecordFootEvent(currentTime, false, true, rightFoot.position);

            if (lastRightContactTime > 0)
            {
                float stepTime = currentTime - lastRightContactTime;
                if (stepTime >= MinReasonableStepTime && stepTime <= MaxReasonableStepTime)
                {
                    lastRightStepTime = stepTime;
                    state.rightStep = 1.0f;
                    state.rightStepTime = stepTime;
                    recentStepTimes.Add(stepTime);
                    if (DebugMode) Debug.Log($"RIGHT STEP: {stepTime:F3}s");
                }
            }
            lastRightContactTime = currentTime;
        }
        else if (!rightContactNow && rightFootInContact)
        {
            rightFootInContact = false;
            if (rightFoot != null) RecordFootEvent(currentTime, false, false, rightFoot.position);
        }
        else
        {
            state.rightStep = 0.0f;
        }

        AnalyzeGaitMetrics(ref state);
    }

    private void RecordFootEvent(float time, bool isLeftFoot, bool isFootDown, Vector3 position)
    {
        footEventHistory.Enqueue(new FootEvent(time, isLeftFoot, isFootDown, position));
        while (footEventHistory.Count > EventHistoryCount)
            footEventHistory.Dequeue();
    }

    private void AnalyzeGaitMetrics(ref CapturyInputState state)
    {
        if (lastLeftStepTime > 0 && lastRightStepTime > 0)
        {
            float stepTimeAsymmetry = Mathf.Abs(lastLeftStepTime - lastRightStepTime) /
                                    ((lastLeftStepTime + lastRightStepTime) / 2f);
            state.stepTimeAsymmetry = stepTimeAsymmetry;

            float averageStepTime = (lastLeftStepTime + lastRightStepTime) / 2f;
            currentCadence = 60f / averageStepTime;
            state.cadence = currentCadence;

            if (recentStepTimes.Count >= MinimumCyclesForAnalysis * 2)
            {
                while (recentStepTimes.Count > 20)
                    recentStepTimes.RemoveAt(0);

                float mean = recentStepTimes.Average();
                float variance = recentStepTimes.Select(t => (t - mean) * (t - mean)).Average();
                float stdDev = Mathf.Sqrt(variance);
                float consistency = Mathf.Clamp01(1f - (stdDev / mean));
                state.gaitConsistency = consistency;
            }
        }
    }

    #endregion

    #region Layer 4: State Output

    private void UpdateWalkState(ref CapturyInputState state)
    {
        state.isWalking = (currentWalkState == WalkState.Walking) ? 1.0f : 0.0f;
        state.walkStarted = (previousWalkState != WalkState.Walking && currentWalkState == WalkState.Walking) ? 1.0f : 0.0f;
        state.walkStopped = (previousWalkState == WalkState.Walking && currentWalkState != WalkState.Walking) ? 1.0f : 0.0f;
        state.walkSpeed = currentWalkSpeed;
    }

    #endregion

    #region Utility Methods

    private void ClearHistoryBuffers()
    {
        spinePositionHistory?.Clear();
        timestampHistory?.Clear();
        footEventHistory?.Clear();
        recentStepTimes?.Clear();

        currentWalkState = WalkState.Idle;
        leftFootInContact = false;
        rightFootInContact = false;
    }

    public float GetCurrentFootDistance()
    {
        string leftFootName = FootConfig?.leftFootJointName ?? "LeftFoot";
        string rightFootName = FootConfig?.rightFootJointName ?? "RightFoot";
        Transform leftFoot = GetJoint(leftFootName);
        Transform rightFoot = GetJoint(rightFootName);

        if (leftFoot == null || rightFoot == null) return 0f;

        Vector2 leftPos2D = new Vector2(leftFoot.position.x, leftFoot.position.z);
        Vector2 rightPos2D = new Vector2(rightFoot.position.x, rightFoot.position.z);
        return Vector2.Distance(leftPos2D, rightPos2D);
    }

    public WalkState GetWalkState() => currentWalkState;
    public float GetCurrentCadence() => currentCadence;

    #endregion
}