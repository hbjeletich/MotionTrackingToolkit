using UnityEngine;
using UnityEngine.InputSystem.LowLevel;

public class HeadTrackingModule : MotionTrackingModule
{
    #region Calibration Data

    [System.Serializable]
    public class HeadCalibrationSnapshot : CalibrationSnapshot
    {
        public Vector3 neutralHeadPosition;
        public Vector3 neutralNeckPosition;
        public Vector3 neutralHeadRotation;
        public Vector3 neutralNeckRotation;
        public Vector3 neutralHeadToNeckOffset;
        public Vector3 neutralHeadToNeckRotationOffset;

        public override CalibrationSnapshot Clone()
        {
            return new HeadCalibrationSnapshot
            {
                timestamp = timestamp,
                neutralHeadPosition = neutralHeadPosition,
                neutralNeckPosition = neutralNeckPosition,
                neutralHeadRotation = neutralHeadRotation,
                neutralNeckRotation = neutralNeckRotation,
                neutralHeadToNeckOffset = neutralHeadToNeckOffset,
                neutralHeadToNeckRotationOffset = neutralHeadToNeckRotationOffset
            };
        }
    }

    #endregion

    #region Variables

    private bool isHeadUp = false;
    private bool isHeadDown = false;
    private bool isHeadLeft = false;
    private bool isHeadRight = false;

    // calibration access
    private HeadModuleConfiguration HeadConfig => GetModuleConfig() as HeadModuleConfiguration;
    private HeadCalibrationSnapshot HeadCalibration => CurrentCalibration as HeadCalibrationSnapshot;

    // config values with fallbacks
    public bool IsHeadPositionTracked => HeadConfig?.isHeadPositionTracked ?? true;
    public bool IsHeadRotationTracked => HeadConfig?.isHeadRotationTracked ?? true;
    public bool IsDirectionDetectionEnabled => HeadConfig?.isHeadDirectionEnabled ?? true;
    public bool UseRelativeHeadPosition => HeadConfig?.useRelativeHeadPosition ?? true;
    public float HeadUpThreshold => HeadConfig?.headUpThreshold ?? 15f;
    public float HeadDownThreshold => HeadConfig?.headDownThreshold ?? 15f;
    public float HeadLeftThreshold => HeadConfig?.headLeftThreshold ?? 20f;
    public float HeadRightThreshold => HeadConfig?.headRightThreshold ?? 20f;

    #endregion

    #region Base Class Implementation

    public override ModuleConfiguration GetModuleConfig()
    {
        return manager?.Config?.GetModuleConfig<HeadModuleConfiguration>();
    }

    protected override CalibrationSnapshot CaptureCalibration()
    {
        var cfg = HeadConfig;
        Transform head = GetJoint(cfg.headJointName);
        Transform neck = GetJoint(cfg.neckJointName);

        if (head == null || neck == null)
        {
            Debug.LogError("HeadTrackingModule: Missing joints during calibration capture");
            return null;
        }

        var snapshot = new HeadCalibrationSnapshot
        {
            neutralHeadPosition = head.position,
            neutralNeckPosition = neck.position,
            neutralHeadRotation = head.eulerAngles,
            neutralNeckRotation = neck.eulerAngles,
            neutralHeadToNeckOffset = head.position - neck.position,
            neutralHeadToNeckRotationOffset = NormalizeEulerAngles(head.eulerAngles - neck.eulerAngles)
        };

        Debug.Log($"HeadTrackingModule: Captured calibration — " +
                 $"Head: {snapshot.neutralHeadPosition:F3}, Neck: {snapshot.neutralNeckPosition:F3}");

        return snapshot;
    }

    protected override void OnCalibrationApplied()
    {
        isHeadUp = false;
        isHeadDown = false;
        isHeadLeft = false;
        isHeadRight = false;
    }

    public override string SerializeCalibration()
    {
        var cal = CurrentCalibration as HeadCalibrationSnapshot;
        if (cal == null) return null;
        return JsonUtility.ToJson(cal);
    }

    public override void DeserializeCalibration(string json)
    {
        if (string.IsNullOrEmpty(json)) return;
        var snapshot = JsonUtility.FromJson<HeadCalibrationSnapshot>(json);
        if (snapshot != null) SetCalibration(snapshot);
    }

    #endregion

    #region Update

    public override void UpdateTracking(ref CapturyInputState state)
    {
        if (!IsEnabled || !IsCalibrated) return;

        var cfg = HeadConfig;
        Transform head = GetJoint(cfg.headJointName);
        Transform neck = GetJoint(cfg.neckJointName);

        if (head == null || neck == null)
        {
            if (DebugMode && Time.frameCount % 300 == 0)
                Debug.Log("HeadTrackingModule: Missing tracked joints");
            return;
        }

        var cal = HeadCalibration;

        // position: head-to-neck offset vs neutral
        Vector3 currentHeadToNeckOffset = head.position - neck.position;
        Vector3 relativePositionMovement = currentHeadToNeckOffset - cal.neutralHeadToNeckOffset;

        // rotation: head-to-neck rotation offset vs neutral
        Vector3 currentHeadToNeckRotationOffset = NormalizeEulerAngles(head.eulerAngles - neck.eulerAngles);
        Vector3 relativeRotationMovement = NormalizeEulerAngles(currentHeadToNeckRotationOffset - cal.neutralHeadToNeckRotationOffset);

        if (IsHeadPositionTracked)
            UpdateHeadPosition(ref state, head, relativePositionMovement);

        if (IsHeadRotationTracked)
            UpdateHeadRotation(ref state, relativeRotationMovement);

        if (IsDirectionDetectionEnabled)
            UpdateHeadDirection(ref state, relativeRotationMovement);
    }

    private void UpdateHeadPosition(ref CapturyInputState state, Transform head, Vector3 relativeMovement)
    {
        if (UseRelativeHeadPosition)
            state.headPosition = relativeMovement * Sensitivity;
        else
            state.headPosition = head.position * Sensitivity;
    }

    private void UpdateHeadRotation(ref CapturyInputState state, Vector3 relativeRotation)
    {
        state.headRotation = relativeRotation * Sensitivity;
    }

    private void UpdateHeadDirection(ref CapturyInputState state, Vector3 relativeRotation)
    {
        float pitchAngle = relativeRotation.z;
        float yawAngle = relativeRotation.y;

        // HEAD UP (negative pitch)
        bool headUpNow = pitchAngle < -HeadUpThreshold;
        if (headUpNow != isHeadUp)
        {
            isHeadUp = headUpNow;
            if (DebugMode) Debug.Log($"HeadTrackingModule: HEAD {(isHeadUp ? "UP" : "NEUTRAL (vertical)")} — Pitch: {pitchAngle:F1}°");
        }
        state.headUp = isHeadUp ? 1.0f : 0.0f;

        // HEAD DOWN (positive pitch)
        bool headDownNow = pitchAngle > HeadDownThreshold;
        if (headDownNow != isHeadDown)
        {
            isHeadDown = headDownNow;
            if (DebugMode) Debug.Log($"HeadTrackingModule: HEAD {(isHeadDown ? "DOWN" : "NEUTRAL (vertical)")} — Pitch: {pitchAngle:F1}°");
        }
        state.headDown = isHeadDown ? 1.0f : 0.0f;

        // HEAD LEFT (negative yaw)
        bool headLeftNow = yawAngle < -HeadLeftThreshold;
        if (headLeftNow != isHeadLeft)
        {
            isHeadLeft = headLeftNow;
            if (DebugMode) Debug.Log($"HeadTrackingModule: HEAD {(isHeadLeft ? "LEFT" : "NEUTRAL (horizontal)")} — Yaw: {yawAngle:F1}°");
        }
        state.headLeft = isHeadLeft ? 1.0f : 0.0f;

        // HEAD RIGHT (positive yaw)
        bool headRightNow = yawAngle > HeadRightThreshold;
        if (headRightNow != isHeadRight)
        {
            isHeadRight = headRightNow;
            if (DebugMode) Debug.Log($"HeadTrackingModule: HEAD {(isHeadRight ? "RIGHT" : "NEUTRAL (horizontal)")} — Yaw: {yawAngle:F1}°");
        }
        state.headRight = isHeadRight ? 1.0f : 0.0f;

        if (DebugMode && Time.frameCount % 120 == 0)
        {
            Debug.Log($"HeadTrackingModule: Pitch={pitchAngle:F1}°, Yaw={yawAngle:F1}° | " +
                     $"Up={isHeadUp}, Down={isHeadDown}, Left={isHeadLeft}, Right={isHeadRight}");
        }
    }

    #endregion

    #region Utility Methods

    public bool GetIsHeadUp() => isHeadUp;
    public bool GetIsHeadDown() => isHeadDown;
    public bool GetIsHeadLeft() => isHeadLeft;
    public bool GetIsHeadRight() => isHeadRight;

    public Vector3 GetCurrentHeadPosition()
    {
        Transform head = GetJoint(HeadConfig?.headJointName ?? "Head");
        return head?.position ?? Vector3.zero;
    }

    public Vector3 GetCurrentHeadRotation()
    {
        Transform head = GetJoint(HeadConfig?.headJointName ?? "Head");
        return head?.eulerAngles ?? Vector3.zero;
    }

    public Vector3 GetRelativeHeadRotation()
    {
        var cfg = HeadConfig;
        var cal = HeadCalibration;
        if (cfg == null || cal == null) return Vector3.zero;

        Transform head = GetJoint(cfg.headJointName);
        Transform neck = GetJoint(cfg.neckJointName);
        if (head == null || neck == null) return Vector3.zero;

        Vector3 currentOffset = NormalizeEulerAngles(head.eulerAngles - neck.eulerAngles);
        return NormalizeEulerAngles(currentOffset - cal.neutralHeadToNeckRotationOffset);
    }

    public Vector3 GetHeadRelativeToNeck()
    {
        var cfg = HeadConfig;
        var cal = HeadCalibration;
        if (cfg == null || cal == null) return Vector3.zero;

        Transform head = GetJoint(cfg.headJointName);
        Transform neck = GetJoint(cfg.neckJointName);
        if (head == null || neck == null) return Vector3.zero;

        return (head.position - neck.position) - cal.neutralHeadToNeckOffset;
    }

    #endregion
}