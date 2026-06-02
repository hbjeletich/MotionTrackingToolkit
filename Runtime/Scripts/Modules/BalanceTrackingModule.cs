using UnityEngine;
using UnityEngine.InputSystem.LowLevel;
using System.Collections.Generic;
using System.Linq;

public class BalanceTrackingModule : MotionTrackingModule
{
    #region Calibration Data

    [System.Serializable]
    public class BalanceCalibrationSnapshot : CalibrationSnapshot
    {
        public Vector3 neutralCoM;
        public float neutralGroundHeight;

        public override CalibrationSnapshot Clone()
        {
            return new BalanceCalibrationSnapshot
            {
                timestamp = timestamp,
                neutralCoM = neutralCoM,
                neutralGroundHeight = neutralGroundHeight
            };
        }
    }

    #endregion

    #region Variables

    // body segment masses (from research — fractions of total body mass)
    private const float TRUNK_MASS = 0.497f;
    private const float FOREARM_MASS = 0.016f;
    private const float LOWER_LEG_MASS = 0.0465f;

    // current CoM tracking
    private Vector3 currentCoM = Vector3.zero;
    private Vector3 previousCoM = Vector3.zero;
    private float comVelocity = 0f;

    // base of support
    private Vector2 baseOfSupportCenter = Vector2.zero;
    private float baseOfSupportWidth = 0f;
    private float comToBaseOfSupportDistance = 0f;

    // foot contact
    private bool leftFootInContact = true;
    private bool rightFootInContact = true;
    private float leftToeHeight = 0f;
    private float rightToeHeight = 0f;

    // balance state
    private bool isBalanced = true;
    private float swayMagnitude = 0f;

    // data buffering
    private Queue<Vector3> comHistory;
    private Queue<float> comTimestamps;

    // calibration access
    private BalanceModuleConfiguration BalanceConfig => GetModuleConfig() as BalanceModuleConfiguration;
    private BalanceCalibrationSnapshot BalanceCalibration => CurrentCalibration as BalanceCalibrationSnapshot;

    // config values with fallbacks
    public bool IsCoMTracked => BalanceConfig?.isCoMTracked ?? true;
    public bool IsSwayTracked => BalanceConfig?.isSwayTracked ?? true;
    public bool IsStabilityTracked => BalanceConfig?.isStabilityTracked ?? true;
    public float SwayThreshold => BalanceConfig?.swayThreshold ?? 0.1f;
    public float StabilityThreshold => BalanceConfig?.stabilityThreshold ?? 0.05f;
    public int CoMHistoryFrames => BalanceConfig?.comHistoryFrames ?? 180;
    public float MinLiftHeight => BalanceConfig?.minLiftHeight ?? 0.05f;

    #endregion

    #region Base Class Implementation

    public override ModuleConfiguration GetModuleConfig()
    {
        return manager?.Config?.GetModuleConfig<BalanceModuleConfiguration>();
    }

    public override void Initialize(IMotionTrackingManager manager)
    {
        base.Initialize(manager);
        comHistory = new Queue<Vector3>();
        comTimestamps = new Queue<float>();
        Debug.Log($"BalanceTrackingModule: Initialized — CoM: {IsCoMTracked}, Sway: {IsSwayTracked}, Stability: {IsStabilityTracked}");
    }

    protected override CalibrationSnapshot CaptureCalibration()
    {
        var cfg = BalanceConfig;

        // resolve all joint transforms for CoM calculation
        Transform trunk = GetJoint(cfg.trunkJointName);
        Transform leftForeArm = GetJoint(cfg.leftForeArmJointName);
        Transform rightForeArm = GetJoint(cfg.rightForeArmJointName);
        Transform leftLeg = GetJoint(cfg.leftLegJointName);
        Transform rightLeg = GetJoint(cfg.rightLegJointName);
        Transform leftToeBase = GetJoint(cfg.leftToeBaseJointName);
        Transform rightToeBase = GetJoint(cfg.rightToeBaseJointName);

        if (trunk == null || leftForeArm == null || rightForeArm == null ||
            leftLeg == null || rightLeg == null || leftToeBase == null || rightToeBase == null)
        {
            Debug.LogError("BalanceTrackingModule: Missing joints during calibration capture");
            return null;
        }

        Vector3 initialCoM = CalculateCenterOfMass(trunk, leftForeArm, rightForeArm, leftLeg, rightLeg);

        var snapshot = new BalanceCalibrationSnapshot
        {
            neutralCoM = initialCoM,
            neutralGroundHeight = Mathf.Min(leftToeBase.position.y, rightToeBase.position.y)
        };

        // reset tracking state
        currentCoM = initialCoM;
        previousCoM = initialCoM;
        leftFootInContact = true;
        rightFootInContact = true;
        UpdateBaseOfSupport();
        comHistory.Clear();
        comTimestamps.Clear();

        Debug.Log($"BalanceTrackingModule: Captured calibration — CoM: {snapshot.neutralCoM:F3}, Ground: {snapshot.neutralGroundHeight:F3}");
        return snapshot;
    }

    protected override void OnCalibrationApplied()
    {
        isBalanced = true;
        swayMagnitude = 0f;
        comHistory?.Clear();
        comTimestamps?.Clear();
    }

    public override string SerializeCalibration()
    {
        var cal = CurrentCalibration as BalanceCalibrationSnapshot;
        if (cal == null) return null;
        return JsonUtility.ToJson(cal);
    }

    public override void DeserializeCalibration(string json)
    {
        if (string.IsNullOrEmpty(json)) return;
        var snapshot = JsonUtility.FromJson<BalanceCalibrationSnapshot>(json);
        if (snapshot != null) SetCalibration(snapshot);
    }

    #endregion

    #region Update

    public override void UpdateTracking(ref CapturyInputState state)
    {
        if (!IsEnabled || !IsCalibrated) return;

        var cfg = BalanceConfig;
        Transform trunk = GetJoint(cfg.trunkJointName);
        Transform leftForeArm = GetJoint(cfg.leftForeArmJointName);
        Transform rightForeArm = GetJoint(cfg.rightForeArmJointName);
        Transform leftLeg = GetJoint(cfg.leftLegJointName);
        Transform rightLeg = GetJoint(cfg.rightLegJointName);

        if (trunk == null || leftForeArm == null || rightForeArm == null ||
            leftLeg == null || rightLeg == null)
        {
            if (DebugMode && Time.frameCount % 300 == 0)
                Debug.Log("BalanceTrackingModule: Missing tracked joints");
            return;
        }

        previousCoM = currentCoM;
        currentCoM = CalculateCenterOfMass(trunk, leftForeArm, rightForeArm, leftLeg, rightLeg);

        UpdateFootContact();
        UpdateBaseOfSupport();
        UpdateDataBuffers();

        if (IsCoMTracked)
            UpdateCoMPosition(ref state);
        if (IsSwayTracked)
            UpdateSwayRelativeToFeet(ref state);
        if (IsStabilityTracked)
            UpdateStabilityWithBaseOfSupport(ref state);
    }

    private Vector3 CalculateCenterOfMass(Transform trunk, Transform leftForeArm, Transform rightForeArm,
                                           Transform leftLeg, Transform rightLeg)
    {
        float totalMass = TRUNK_MASS + (2 * FOREARM_MASS) + (2 * LOWER_LEG_MASS);

        Vector3 weightedSum = Vector3.zero;
        weightedSum += trunk.position * TRUNK_MASS;
        weightedSum += leftForeArm.position * FOREARM_MASS;
        weightedSum += rightForeArm.position * FOREARM_MASS;
        weightedSum += leftLeg.position * LOWER_LEG_MASS;
        weightedSum += rightLeg.position * LOWER_LEG_MASS;

        return weightedSum / totalMass;
    }

    private void UpdateFootContact()
    {
        var cfg = BalanceConfig;
        var cal = BalanceCalibration;

        Transform leftToeBase = GetJoint(cfg.leftToeBaseJointName);
        Transform rightToeBase = GetJoint(cfg.rightToeBaseJointName);
        if (leftToeBase == null || rightToeBase == null) return;

        leftToeHeight = leftToeBase.position.y - cal.neutralGroundHeight;
        rightToeHeight = rightToeBase.position.y - cal.neutralGroundHeight;

        float contactThreshold = MinLiftHeight * 0.5f;

        bool leftContactNow = leftToeHeight < contactThreshold;
        bool rightContactNow = rightToeHeight < contactThreshold;

        if (leftContactNow != leftFootInContact)
        {
            leftFootInContact = leftContactNow;
            if (DebugMode) Debug.Log($"BalanceTrackingModule: Left foot contact → {leftContactNow}");
        }
        if (rightContactNow != rightFootInContact)
        {
            rightFootInContact = rightContactNow;
            if (DebugMode) Debug.Log($"BalanceTrackingModule: Right foot contact → {rightContactNow}");
        }
    }

    private void UpdateBaseOfSupport()
    {
        var cfg = BalanceConfig;
        Transform leftToeBase = GetJoint(cfg.leftToeBaseJointName);
        Transform rightToeBase = GetJoint(cfg.rightToeBaseJointName);
        if (leftToeBase == null || rightToeBase == null) return;

        Vector2 leftToePos2D = new Vector2(leftToeBase.position.x, leftToeBase.position.z);
        Vector2 rightToePos2D = new Vector2(rightToeBase.position.x, rightToeBase.position.z);

        if (leftFootInContact && rightFootInContact)
        {
            baseOfSupportCenter = (leftToePos2D + rightToePos2D) / 2f;
            baseOfSupportWidth = Vector2.Distance(leftToePos2D, rightToePos2D);
        }
        else if (leftFootInContact)
        {
            baseOfSupportCenter = leftToePos2D;
            baseOfSupportWidth = 0.1f;
        }
        else if (rightFootInContact)
        {
            baseOfSupportCenter = rightToePos2D;
            baseOfSupportWidth = 0.1f;
        }
        // else: no feet on ground, maintain last values

        Vector2 comProjection = new Vector2(currentCoM.x, currentCoM.z);
        comToBaseOfSupportDistance = Vector2.Distance(comProjection, baseOfSupportCenter);
    }

    private void UpdateDataBuffers()
    {
        comHistory.Enqueue(currentCoM);
        comTimestamps.Enqueue(Time.time);

        while (comHistory.Count > CoMHistoryFrames)
        {
            comHistory.Dequeue();
            comTimestamps.Dequeue();
        }
    }

    private void UpdateCoMPosition(ref CapturyInputState state)
    {
        var cal = BalanceCalibration;

        Vector2 comProjection = new Vector2(currentCoM.x, currentCoM.z);
        Vector2 comRelativeToBase = comProjection - baseOfSupportCenter;

        Vector3 comRelativeToSupport = new Vector3(
            comRelativeToBase.x,
            currentCoM.y - cal.neutralCoM.y,
            comRelativeToBase.y
        );

        state.centerOfMassPosition = comRelativeToSupport * Sensitivity;

        if (DebugMode && Time.frameCount % 60 == 0)
            Debug.Log($"BalanceTrackingModule: CoM offset from base center: {comRelativeToBase.magnitude:F3}m");
    }

    private void UpdateSwayRelativeToFeet(ref CapturyInputState state)
    {
        Vector2 comProjection = new Vector2(currentCoM.x, currentCoM.z);
        Vector2 swayVector = comProjection - baseOfSupportCenter;

        float lateralSway = swayVector.x;
        float apSway = swayVector.y;

        state.lateralSway = lateralSway * Sensitivity;
        state.anteriorPosteriorSway = apSway * Sensitivity;

        swayMagnitude = swayVector.magnitude;
        state.swayMagnitude = swayMagnitude;

        float swayThresholdMultiplier;
        if (leftFootInContact && rightFootInContact) swayThresholdMultiplier = 0.6f;
        else if (leftFootInContact || rightFootInContact) swayThresholdMultiplier = 0.8f;
        else swayThresholdMultiplier = 0.0f;

        float normalizedSway = baseOfSupportWidth > 0 ? swayMagnitude / (baseOfSupportWidth * 0.5f)
                                                       : swayMagnitude / 0.05f;

        bool swayingNow = normalizedSway > swayThresholdMultiplier;
        state.isSwaying = swayingNow ? 1.0f : 0.0f;

        if (DebugMode && (swayingNow || Time.frameCount % 120 == 0))
        {
            Debug.Log($"BalanceTrackingModule: Lateral: {lateralSway:F3}, AP: {apSway:F3}, " +
                     $"Total: {swayMagnitude:F3}, Normalized: {normalizedSway:F2}, " +
                     $"BaseWidth: {baseOfSupportWidth:F3}, Contact: L={leftFootInContact} R={rightFootInContact}");
        }
    }

    private void UpdateStabilityWithBaseOfSupport(ref CapturyInputState state)
    {
        if (comHistory.Count < 2) return;

        Vector3 comDisplacement = currentCoM - previousCoM;
        comVelocity = comDisplacement.magnitude / Time.deltaTime;
        state.comVelocity = comVelocity;

        bool hasLowVelocity = comVelocity < StabilityThreshold;

        float maxAllowedDistance;
        if (leftFootInContact && rightFootInContact) maxAllowedDistance = baseOfSupportWidth * 0.4f;
        else if (leftFootInContact || rightFootInContact) maxAllowedDistance = baseOfSupportWidth * 0.6f;
        else maxAllowedDistance = 0.0f;

        bool withinBaseOfSupport = comToBaseOfSupportDistance < maxAllowedDistance;

        bool wasBalanced = isBalanced;
        isBalanced = hasLowVelocity && withinBaseOfSupport;

        state.isBalanced = isBalanced ? 1.0f : 0.0f;
        state.balanceLost = (!isBalanced && wasBalanced) ? 1.0f : 0.0f;
        state.balanceRegained = (isBalanced && !wasBalanced) ? 1.0f : 0.0f;

        if (DebugMode && (!isBalanced || Time.frameCount % 120 == 0))
        {
            Debug.Log($"BalanceTrackingModule: Velocity: {comVelocity:F3} m/s, " +
                     $"CoM Dist: {comToBaseOfSupportDistance:F3}m, Max: {maxAllowedDistance:F3}m, " +
                     $"Balanced: {isBalanced}, Contact: L={leftFootInContact} R={rightFootInContact}");
        }
    }

    #endregion

    #region Utility Methods

    public Vector3 GetCurrentCoM() => currentCoM;
    public Vector3 GetCoMRelativeToNeutral() => currentCoM - (BalanceCalibration?.neutralCoM ?? Vector3.zero);

    public Vector2 GetCoMRelativeToBaseOfSupport()
    {
        Vector2 comProjection = new Vector2(currentCoM.x, currentCoM.z);
        return comProjection - baseOfSupportCenter;
    }

    public float GetSwayMagnitude() => swayMagnitude;
    public float GetCoMVelocity() => comVelocity;
    public bool GetIsBalanced() => isBalanced;
    public float GetBaseOfSupportWidth() => baseOfSupportWidth;
    public Vector2 GetBaseOfSupportCenter() => baseOfSupportCenter;
    public float GetDistanceFromBaseCenter() => comToBaseOfSupportDistance;
    public bool GetLeftFootInContact() => leftFootInContact;
    public bool GetRightFootInContact() => rightFootInContact;

    public string GetContactState()
    {
        if (leftFootInContact && rightFootInContact) return "Both Feet";
        if (leftFootInContact) return "Left Foot Only";
        if (rightFootInContact) return "Right Foot Only";
        return "No Contact";
    }

    public Vector2 GetSwayComponents()
    {
        Vector2 comProjection = new Vector2(currentCoM.x, currentCoM.z);
        return comProjection - baseOfSupportCenter;
    }

    #endregion
}