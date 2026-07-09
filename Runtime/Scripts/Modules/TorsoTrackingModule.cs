using UnityEngine;
using UnityEngine.InputSystem.LowLevel;

public class TorsoTrackingModule : MotionTrackingModule
{
    #region Calibration Data

    [System.Serializable]
    public class TorsoCalibrationSnapshot : CalibrationSnapshot
    {
        public Vector3 neutralPelvisPosition;
        public Vector3 neutralPelvisRotation;
        public Vector3 neutralSpinePosition;
        public Vector3 neutralSpineToPelvisOffset;
        public float neutralHipKneeDistance;
        // Neutral knee angles stored per-leg so each knee is compared to its own baseline.
        // neutralHipKneeRatio is the average (kept for backward-compat serialization).
        public float neutralHipKneeRatio;
        public float neutralLeftKneeAngle;
        public float neutralRightKneeAngle;

        public override CalibrationSnapshot Clone()
        {
            return new TorsoCalibrationSnapshot
            {
                timestamp = timestamp,
                neutralPelvisPosition = neutralPelvisPosition,
                neutralPelvisRotation = neutralPelvisRotation,
                neutralSpinePosition = neutralSpinePosition,
                neutralSpineToPelvisOffset = neutralSpineToPelvisOffset,
                neutralHipKneeDistance = neutralHipKneeDistance,
                neutralHipKneeRatio = neutralHipKneeRatio,
                neutralLeftKneeAngle = neutralLeftKneeAngle,
                neutralRightKneeAngle = neutralRightKneeAngle,
            };
        }
    }

    #endregion

    #region Variables

    // internal states
    private bool isShiftingLeft = false;
    private bool isShiftingRight = false;
    private bool isBentOver = false;
    private bool isSquatting = false;
    private float _smoothedSquatDepth = 0f;

    // calibration access
    private TorsoModuleConfiguration TorsoConfig => GetModuleConfig() as TorsoModuleConfiguration;
    private TorsoCalibrationSnapshot TorsoCalibration => CurrentCalibration as TorsoCalibrationSnapshot;

    // config values with fallbacks
    public bool IsShiftTracked => TorsoConfig?.isShiftTracked ?? false;
    public bool IsBendTracked => TorsoConfig?.isBendTracked ?? false;
    public bool IsSquatTracked => TorsoConfig?.isSquatTracked ?? false;
    public float WeightShiftThreshold => TorsoConfig?.weightShiftThreshold ?? 0.15f;
    public float NeutralZoneWidth => TorsoConfig?.neutralZoneWidth ?? 0.05f;
    public float BentOverAngleThreshold => TorsoConfig?.bentOverAngleThreshold ?? 30f;
    public float WholeBodyMovementThreshold => TorsoConfig?.wholeBodyMovementThreshold ?? 3f;
    public float SquatThreshold => TorsoConfig?.squatThreshold ?? 0.12f;

    #endregion

    #region Base Class Implementation

    public override ModuleConfiguration GetModuleConfig()
    {
        return manager?.Config?.GetModuleConfig<TorsoModuleConfiguration>();
    }

    protected override CalibrationSnapshot CaptureCalibration()
    {
        string pelvisName = TorsoConfig?.pelvisJointName ?? "Hips";
        string spineName = TorsoConfig?.spineJointName ?? "Spine4";

        Transform pelvis = GetJoint(pelvisName);
        Transform spine = GetJoint(spineName);

        if (pelvis == null || spine == null)
        {
            Debug.LogError("TorsoTrackingModule: Missing joints during calibration capture");
            return null;
        }

        float hipKneeDist             = 0f;
        float hipKneeRatio            = 0f;
        float neutralLeftKneeAngle    = 0f;
        float neutralRightKneeAngle   = 0f;
        if (IsSquatTracked)
        {
            var mpManager = manager as MediaPipeMotionTrackingManager;

            // Priority 1: Knee angle from world landmarks — scale-invariant, works for all modes.
            if (mpManager != null && mpManager.HasWorldKeyLandmarks && mpManager.HasWorldKeyAnkles)
            {
                var wkl = mpManager.WorldKeyLandmarks;
                // wkl[0]=LHip, wkl[1]=RHip, wkl[2]=LKnee, wkl[3]=RKnee, wkl[4]=LAnkle, wkl[5]=RAnkle
                float lAngle = Vector3.Angle(wkl[0] - wkl[2], wkl[4] - wkl[2]);
                float rAngle = Vector3.Angle(wkl[1] - wkl[3], wkl[5] - wkl[3]);
                hipKneeRatio = (lAngle + rAngle) * 0.5f;
                neutralLeftKneeAngle  = lAngle;
                neutralRightKneeAngle = rAngle;
                if (DebugMode) Debug.Log($"[SQUAT CAL] Knee angle — L={lAngle:F1}° R={rAngle:F1}° neutral={hipKneeRatio:F1}°");
            }
            // Priority 2: Joint path fallback (Captury/Kinect or no MediaPipe data).
            else
            {
                Transform leftKnee  = GetJoint(TorsoConfig.leftKneeJointName);
                Transform rightKnee = GetJoint(TorsoConfig.rightKneeJointName);
                if (leftKnee != null && rightKnee != null)
                {
                    Vector3 kneeCenter = (leftKnee.position + rightKnee.position) * 0.5f;
                    var fp = GetFloorPlane();
                    if (fp.HasValue)
                    {
                        hipKneeDist = fp.Value.GetDistanceToPoint(pelvis.position)
                                    - fp.Value.GetDistanceToPoint(kneeCenter);
                        if (DebugMode) Debug.Log($"[SQUAT CAL] Joint+plane path — hipKneeDist={hipKneeDist:F3}m");
                    }
                    else
                    {
                        hipKneeDist = pelvis.position.y - kneeCenter.y;
                        if (DebugMode) Debug.Log($"[SQUAT CAL] Joint path — hipKneeDist={hipKneeDist:F3}m");
                    }
                }
                else
                {
                    Debug.LogWarning($"[SQUAT CAL] No pixel landmarks, no world landmarks, and no knee joints — squat tracking will not work.");
                }
            }
        }

        var snapshot = new TorsoCalibrationSnapshot
        {
            neutralPelvisPosition = pelvis.position,
            neutralPelvisRotation = pelvis.eulerAngles,
            neutralSpinePosition = spine.position,
            neutralSpineToPelvisOffset = spine.position - pelvis.position,
            neutralHipKneeDistance  = hipKneeDist,
            neutralHipKneeRatio     = hipKneeRatio,
            neutralLeftKneeAngle    = neutralLeftKneeAngle,
            neutralRightKneeAngle   = neutralRightKneeAngle,
        };

        Debug.Log($"TorsoTrackingModule: Captured calibration — " +
                 $"Pelvis: {snapshot.neutralPelvisPosition:F3}, Spine: {snapshot.neutralSpinePosition:F3}, " +
                 $"Offset: {snapshot.neutralSpineToPelvisOffset:F3}" +
                 (IsSquatTracked ? $", HipKneeDist: {hipKneeDist:F3}m" : ""));

        return snapshot;
    }

    protected override void OnCalibrationApplied()
    {
        // reset internal state when calibration changes
        isShiftingLeft = false;
        isShiftingRight = false;
        isBentOver = false;
        isSquatting = false;
    }

    public override string SerializeCalibration()
    {
        var cal = CurrentCalibration as TorsoCalibrationSnapshot;
        if (cal == null) return null;
        return JsonUtility.ToJson(cal);
    }

    public override void DeserializeCalibration(string json)
    {
        if (string.IsNullOrEmpty(json)) return;
        var snapshot = JsonUtility.FromJson<TorsoCalibrationSnapshot>(json);
        if (snapshot != null) SetCalibration(snapshot);
    }

    #endregion

    #region Update

    public override void UpdateTracking(ref CapturyInputState state)
    {
        if (!IsEnabled || !IsCalibrated)
        {
            if (DebugMode && Time.frameCount % 300 == 0)
            {
                if (!IsEnabled) Debug.Log("TorsoTrackingModule: Module disabled");
                if (!IsCalibrated) Debug.Log("TorsoTrackingModule: Module not calibrated");
            }
            return;
        }

        string pelvisName = TorsoConfig.pelvisJointName;
        string spineName = TorsoConfig.spineJointName;
        Transform pelvis = GetJoint(pelvisName);
        Transform spine = GetJoint(spineName);

        if (pelvis == null || spine == null)
        {
            if (DebugMode && Time.frameCount % 300 == 0)
                Debug.Log($"TorsoTrackingModule: Missing tracked joints — Pelvis: {pelvis != null}, Spine: {spine != null}");
            return;
        }

        var cal = TorsoCalibration;

        // calculate relative positions
        Vector3 currentPelvisPosition = pelvis.position;
        Vector3 currentSpinePosition = spine.position;

        Vector3 pelvisMovement = currentPelvisPosition - cal.neutralPelvisPosition;
        Vector3 spineMovement = currentSpinePosition - cal.neutralSpinePosition;

        // current spine-to-pelvis offset vs neutral
        Vector3 currentSpineToPelvisOffset = currentSpinePosition - currentPelvisPosition;
        Vector3 relativeMovement = currentSpineToPelvisOffset - cal.neutralSpineToPelvisOffset;

        // backwards compat: absolute pelvis movement
        state.pelvisPosition = pelvisMovement;

        // rotation
        Vector3 currentRotation = pelvis.eulerAngles;
        Vector3 relativeRotation = NormalizeEulerAngles(currentRotation - cal.neutralPelvisRotation);

        if (IsShiftTracked)
            UpdateWeightShiftRelative(ref state, pelvisMovement, spineMovement, relativeMovement);

        if (IsBendTracked)
            UpdateBentOver(ref state, relativeRotation);

        if (IsSquatTracked)
            UpdateSquat(ref state, pelvis);
    }

    private void UpdateWeightShiftRelative(ref CapturyInputState state, Vector3 pelvisMovement, Vector3 spineMovement, Vector3 relativeMovement)
    {
        float pelvisXMovement = pelvisMovement.x;
        float spineXMovement = spineMovement.x;

        bool isWholeBodyMovement = false;
        if (Mathf.Abs(pelvisXMovement) > 0.01f && Mathf.Abs(spineXMovement) > 0.01f)
        {
            float movementRatio = Mathf.Abs(spineXMovement / pelvisXMovement);
            isWholeBodyMovement = movementRatio > WholeBodyMovementThreshold;
        }

        float shiftAmount = relativeMovement.x * Sensitivity;

        if (isWholeBodyMovement)
        {
            shiftAmount = 0f;
            if (DebugMode && Time.frameCount % 60 == 0)
                Debug.Log($"TorsoTrackingModule: Whole-body movement detected — ignoring weight shift");
        }

        state.weightShiftX = Mathf.Clamp(shiftAmount / WeightShiftThreshold, -1f, 1f);

        bool isInNeutralZone = Mathf.Abs(shiftAmount) < NeutralZoneWidth;

        if (shiftAmount < -NeutralZoneWidth && !isShiftingLeft)
        {
            isShiftingLeft = true;
            isShiftingRight = false;
            state.weightShiftLeft = 1.0f;
            state.weightShiftRight = 0.0f;

            if (DebugMode)
                Debug.Log($"TorsoTrackingModule: WEIGHT SHIFT LEFT — Relative X: {relativeMovement.x:F3}, Adjusted: {shiftAmount:F3}");
        }
        else if (shiftAmount > NeutralZoneWidth && !isShiftingRight)
        {
            isShiftingRight = true;
            isShiftingLeft = false;
            state.weightShiftRight = 1.0f;
            state.weightShiftLeft = 0.0f;

            if (DebugMode)
                Debug.Log($"TorsoTrackingModule: WEIGHT SHIFT RIGHT — Relative X: {relativeMovement.x:F3}, Adjusted: {shiftAmount:F3}");
        }
        else if (isInNeutralZone && (isShiftingLeft || isShiftingRight))
        {
            isShiftingLeft = false;
            isShiftingRight = false;
            state.weightShiftLeft = 0.0f;
            state.weightShiftRight = 0.0f;

            if (DebugMode)
                Debug.Log($"TorsoTrackingModule: Weight returned to NEUTRAL");
        }
        else
        {
            state.weightShiftLeft = isShiftingLeft ? 1.0f : 0.0f;
            state.weightShiftRight = isShiftingRight ? 1.0f : 0.0f;
        }
    }

    private void UpdateBentOver(ref CapturyInputState state, Vector3 relativeRotation)
    {
        float xRotationDiff = Mathf.Abs(relativeRotation.x);
        bool currentlyBentOver = xRotationDiff > BentOverAngleThreshold;

        state.isBentOver = currentlyBentOver ? 1.0f : 0.0f;
        state.isUpright = currentlyBentOver ? 0.0f : 1.0f;

        if (DebugMode && (xRotationDiff > BentOverAngleThreshold * 0.7f || Time.frameCount % 120 == 0))
            Debug.Log($"BentOver: XRotDiff={xRotationDiff:F1}, Threshold={BentOverAngleThreshold:F1}, BentOver={currentlyBentOver}");

        if (currentlyBentOver != isBentOver)
        {
            isBentOver = currentlyBentOver;
            if (DebugMode)
                Debug.Log($"TorsoTrackingModule: Posture changed to {(isBentOver ? "BENT OVER" : "UPRIGHT")}");
        }
    }

    private void UpdateSquat(ref CapturyInputState state, Transform pelvis)
    {
        var cal = TorsoCalibration;
        bool log = DebugMode && Time.frameCount % 60 == 0;
        var mpManager = manager as MediaPipeMotionTrackingManager;

        // Priority 1: Knee angle from world landmarks — scale-invariant, works for all modes.
        // Uses the minimum of the two individual knee drops so a leg lift (one knee bends,
        // other stays straight) produces zero squat depth rather than a false positive.
        if (mpManager != null && mpManager.HasWorldKeyLandmarks && mpManager.HasWorldKeyAnkles && cal.neutralHipKneeRatio > 0f)
        {
            var wkl = mpManager.WorldKeyLandmarks;
            float lAngle = Vector3.Angle(wkl[0] - wkl[2], wkl[4] - wkl[2]);
            float rAngle = Vector3.Angle(wkl[1] - wkl[3], wkl[5] - wkl[3]);
            float lDrop = Mathf.Max(0f, cal.neutralLeftKneeAngle  - lAngle);
            float rDrop = Mathf.Max(0f, cal.neutralRightKneeAngle - rAngle);
            float minDrop = Mathf.Min(lDrop, rDrop);
            float maxDrop = Mathf.Max(lDrop, rDrop);
            // If both knees are bending at similar angles it's a squat — use max for best signal.
            // If one leg is bending much more than the other it's a leg lift — use min to suppress.
            // Only run the symmetry test when both drops are meaningfully large (> 5°).
            // Below that threshold, default to "symmetric" so tiny noise doesn't trigger the leg-lift branch.
            float ratio = maxDrop > 5f ? minDrop / maxDrop : 1f;
            float angleDrop = ratio >= 0.6f ? maxDrop : 0f;
            float rawDepth = Mathf.Clamp01(angleDrop / 80f);
            _smoothedSquatDepth = Mathf.Lerp(_smoothedSquatDepth, rawDepth, Time.deltaTime * 8f);
            if (log) Debug.Log($"[SQUAT] Knee angle — neutral={cal.neutralHipKneeRatio:F1}° L={lAngle:F1}°(drop={lDrop:F1}) R={rAngle:F1}°(drop={rDrop:F1}) ratio={ratio:F2} drop={angleDrop:F1}° depth={_smoothedSquatDepth:F3}");
            ApplySquatState(ref state, _smoothedSquatDepth);
            return;
        }

        // Priority 2: Joint path fallback (Captury/Kinect or no MediaPipe data).
        Transform leftKnee  = GetJoint(TorsoConfig.leftKneeJointName);
        Transform rightKnee = GetJoint(TorsoConfig.rightKneeJointName);
        if (leftKnee == null || rightKnee == null)
        {
            if (log) Debug.Log($"[SQUAT] Joint path — knee joints null");
            state.squatDepth = 0f;
            state.isSquatting = 0f;
            return;
        }
        Vector3 kneeCenter = (leftKnee.position + rightKnee.position) * 0.5f;
        var fp = GetFloorPlane();
        float currentDist = fp.HasValue
            ? fp.Value.GetDistanceToPoint(pelvis.position) - fp.Value.GetDistanceToPoint(kneeCenter)
            : pelvis.position.y - kneeCenter.y;
        float jointDepth = Mathf.Max(0f, cal.neutralHipKneeDistance - currentDist);
        if (log) Debug.Log($"[SQUAT] Joint path — neutral={cal.neutralHipKneeDistance:F3} current={currentDist:F3} depth={jointDepth:F3} floorPlane={fp.HasValue}");
        ApplySquatState(ref state, jointDepth);
    }

    private static Plane? GetFloorPlane()
    {
        var src = MotionTrackingOrchestrator.Instance as IRoomFrameSource;
        if (src == null || !src.HasRoomFrame) return null;
        return src.CurrentFrame?.floorPlane;
    }

    private void ApplySquatState(ref CapturyInputState state, float depth)
    {
        state.squatDepth = depth;

        float threshold = SquatThreshold;
        bool currentlySquatting = depth > threshold;
        state.isSquatting = currentlySquatting ? 1f : 0f;

        if (currentlySquatting != isSquatting)
        {
            isSquatting = currentlySquatting;
            if (DebugMode)
                Debug.Log($"TorsoTrackingModule: Squat {(isSquatting ? "START" : "END")} — depth={depth:F3}, threshold={threshold:F3}");
        }

        if (DebugMode && Time.frameCount % 60 == 0)
            Debug.Log($"TorsoTrackingModule: SquatDepth={depth:F3}");
    }

    #endregion
}