using UnityEngine;
using UnityEngine.InputSystem.LowLevel;

public class ArmTrackingModule : MotionTrackingModule
{
    #region Calibration Data

    [System.Serializable]
    public class ArmCalibrationSnapshot : CalibrationSnapshot
    {
        public Vector3 neutralLeftHandPosition;
        public Vector3 neutralRightHandPosition;
        public Vector3 neutralLeftShoulderPosition;
        public Vector3 neutralRightShoulderPosition;
        public Vector3 neutralLeftHandToShoulderOffset;
        public Vector3 neutralRightHandToShoulderOffset;

        public override CalibrationSnapshot Clone()
        {
            return new ArmCalibrationSnapshot
            {
                timestamp = timestamp,
                neutralLeftHandPosition = neutralLeftHandPosition,
                neutralRightHandPosition = neutralRightHandPosition,
                neutralLeftShoulderPosition = neutralLeftShoulderPosition,
                neutralRightShoulderPosition = neutralRightShoulderPosition,
                neutralLeftHandToShoulderOffset = neutralLeftHandToShoulderOffset,
                neutralRightHandToShoulderOffset = neutralRightHandToShoulderOffset
            };
        }
    }

    #endregion

    #region Variables

    private bool isLeftHandRaised = false;
    private bool isRightHandRaised = false;

    // calibration access
    private ArmModuleConfiguration ArmConfig => GetModuleConfig() as ArmModuleConfiguration;
    private ArmCalibrationSnapshot ArmCalibration => CurrentCalibration as ArmCalibrationSnapshot;

    // config values with fallbacks
    public bool IsHandPositionTracked => ArmConfig?.isHandPositionTracked ?? true;
    public bool IsHandRaiseTracked => ArmConfig?.isHandRaiseTracked ?? true;
    public bool UseRelativeHandPosition => ArmConfig?.useRelativeHandPosition ?? true;
    public float HandRaiseThreshold => ArmConfig?.handRaiseThreshold ?? 0.3f;
    public float HandRaiseMinHeight => ArmConfig?.handRaiseMinHeight ?? 0.1f;

    #endregion

    #region Base Class Implementation

    public override ModuleConfiguration GetModuleConfig()
    {
        return manager?.Config?.GetModuleConfig<ArmModuleConfiguration>();
    }

    protected override CalibrationSnapshot CaptureCalibration()
    {
        var cfg = ArmConfig;
        Transform leftHand = GetJoint(cfg.leftHandJointName);
        Transform rightHand = GetJoint(cfg.rightHandJointName);
        Transform leftShoulder = GetJoint(cfg.leftShoulderJointName);
        Transform rightShoulder = GetJoint(cfg.rightShoulderJointName);

        if (leftHand == null || rightHand == null || leftShoulder == null || rightShoulder == null)
        {
            Debug.LogError("ArmTrackingModule: Missing joints during calibration capture");
            return null;
        }

        var snapshot = new ArmCalibrationSnapshot
        {
            neutralLeftHandPosition = leftHand.position,
            neutralRightHandPosition = rightHand.position,
            neutralLeftShoulderPosition = leftShoulder.position,
            neutralRightShoulderPosition = rightShoulder.position,
            neutralLeftHandToShoulderOffset = leftHand.position - leftShoulder.position,
            neutralRightHandToShoulderOffset = rightHand.position - rightShoulder.position
        };

        Debug.Log($"ArmTrackingModule: Captured calibration — " +
                 $"LHand: {snapshot.neutralLeftHandPosition:F3}, RHand: {snapshot.neutralRightHandPosition:F3}");

        return snapshot;
    }

    protected override void OnCalibrationApplied()
    {
        isLeftHandRaised = false;
        isRightHandRaised = false;
    }

    public override string SerializeCalibration()
    {
        var cal = CurrentCalibration as ArmCalibrationSnapshot;
        if (cal == null) return null;
        return JsonUtility.ToJson(cal);
    }

    public override void DeserializeCalibration(string json)
    {
        if (string.IsNullOrEmpty(json)) return;
        var snapshot = JsonUtility.FromJson<ArmCalibrationSnapshot>(json);
        if (snapshot != null) SetCalibration(snapshot);
    }

    #endregion

    #region Update

    public override void UpdateTracking(ref CapturyInputState state)
    {
        if (!IsEnabled || !IsCalibrated) return;

        var cfg = ArmConfig;
        Transform leftHand = GetJoint(cfg.leftHandJointName);
        Transform rightHand = GetJoint(cfg.rightHandJointName);
        Transform leftShoulder = GetJoint(cfg.leftShoulderJointName);
        Transform rightShoulder = GetJoint(cfg.rightShoulderJointName);

        if (leftHand == null || rightHand == null || leftShoulder == null || rightShoulder == null)
        {
            if (DebugMode && Time.frameCount % 300 == 0)
                Debug.Log("ArmTrackingModule: Missing tracked joints");
            return;
        }

        var cal = ArmCalibration;

        // current hand-to-shoulder offsets
        Vector3 currentLeftOffset = leftHand.position - leftShoulder.position;
        Vector3 currentRightOffset = rightHand.position - rightShoulder.position;

        // relative movement from neutral
        Vector3 leftRelativeMovement = currentLeftOffset - cal.neutralLeftHandToShoulderOffset;
        Vector3 rightRelativeMovement = currentRightOffset - cal.neutralRightHandToShoulderOffset;

        if (IsHandPositionTracked)
            UpdateHandPositions(ref state, leftHand, rightHand, leftRelativeMovement, rightRelativeMovement);

        if (IsHandRaiseTracked)
            UpdateHandRaise(ref state, leftHand.position, rightHand.position,
                          leftShoulder.position, rightShoulder.position);
    }

    private void UpdateHandPositions(ref CapturyInputState state, Transform leftHand, Transform rightHand,
                                     Vector3 leftRelativeMovement, Vector3 rightRelativeMovement)
    {
        if (UseRelativeHandPosition)
        {
            state.leftHandPosition = leftRelativeMovement * Sensitivity;
            state.rightHandPosition = rightRelativeMovement * Sensitivity;
        }
        else
        {
            state.leftHandPosition = leftHand.position * Sensitivity;
            state.rightHandPosition = rightHand.position * Sensitivity;
        }
    }

    private void UpdateHandRaise(ref CapturyInputState state, Vector3 leftHandPos, Vector3 rightHandPos,
                                Vector3 leftShoulderPos, Vector3 rightShoulderPos)
    {
        var cal = ArmCalibration;

        float leftHandRelativeHeight = leftHandPos.y - leftShoulderPos.y;
        float rightHandRelativeHeight = rightHandPos.y - rightShoulderPos.y;

        float leftHandHeightGain = leftHandPos.y - cal.neutralLeftHandPosition.y;
        float rightHandHeightGain = rightHandPos.y - cal.neutralRightHandPosition.y;

        // left hand
        bool leftHandRaisedNow = (leftHandRelativeHeight > HandRaiseThreshold) &&
                                 (leftHandHeightGain > HandRaiseMinHeight);

        if (leftHandRaisedNow && !isLeftHandRaised)
        {
            isLeftHandRaised = true;
            state.leftHandRaised = 1.0f;
            if (DebugMode)
                Debug.Log($"ArmTrackingModule: LEFT HAND RAISED! RelHeight: {leftHandRelativeHeight:F3}");
        }
        else if (!leftHandRaisedNow && isLeftHandRaised)
        {
            isLeftHandRaised = false;
            state.leftHandRaised = 0.0f;
            if (DebugMode) Debug.Log("ArmTrackingModule: LEFT HAND LOWERED!");
        }
        else
        {
            state.leftHandRaised = isLeftHandRaised ? 1.0f : 0.0f;
        }

        // right hand
        bool rightHandRaisedNow = (rightHandRelativeHeight > HandRaiseThreshold) &&
                                  (rightHandHeightGain > HandRaiseMinHeight);

        if (rightHandRaisedNow && !isRightHandRaised)
        {
            isRightHandRaised = true;
            state.rightHandRaised = 1.0f;
            if (DebugMode)
                Debug.Log($"ArmTrackingModule: RIGHT HAND RAISED! RelHeight: {rightHandRelativeHeight:F3}");
        }
        else if (!rightHandRaisedNow && isRightHandRaised)
        {
            isRightHandRaised = false;
            state.rightHandRaised = 0.0f;
            if (DebugMode) Debug.Log("ArmTrackingModule: RIGHT HAND LOWERED!");
        }
        else
        {
            state.rightHandRaised = isRightHandRaised ? 1.0f : 0.0f;
        }
    }

    #endregion

    #region Utility Methods

    public bool GetLeftHandRaised() => isLeftHandRaised;
    public bool GetRightHandRaised() => isRightHandRaised;

    public Vector3 GetCurrentLeftHandPosition()
    {
        Transform lh = GetJoint(ArmConfig?.leftHandJointName ?? "LeftHand");
        return lh?.position ?? Vector3.zero;
    }

    public Vector3 GetCurrentRightHandPosition()
    {
        Transform rh = GetJoint(ArmConfig?.rightHandJointName ?? "RightHand");
        return rh?.position ?? Vector3.zero;
    }

    public Vector3 GetLeftHandRelativeToShoulder()
    {
        var cfg = ArmConfig;
        if (cfg == null || ArmCalibration == null) return Vector3.zero;
        Transform lh = GetJoint(cfg.leftHandJointName);
        Transform ls = GetJoint(cfg.leftShoulderJointName);
        if (lh == null || ls == null) return Vector3.zero;
        return (lh.position - ls.position) - ArmCalibration.neutralLeftHandToShoulderOffset;
    }

    public Vector3 GetRightHandRelativeToShoulder()
    {
        var cfg = ArmConfig;
        if (cfg == null || ArmCalibration == null) return Vector3.zero;
        Transform rh = GetJoint(cfg.rightHandJointName);
        Transform rs = GetJoint(cfg.rightShoulderJointName);
        if (rh == null || rs == null) return Vector3.zero;
        return (rh.position - rs.position) - ArmCalibration.neutralRightHandToShoulderOffset;
    }

    #endregion
}