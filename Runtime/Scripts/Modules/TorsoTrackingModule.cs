using UnityEngine;
using UnityEngine.InputSystem.LowLevel;

/// <summary>
/// Torso-level gestures, all measured relative to the calibrated neutral pose:
///   - Weight shift: sideways change in the spine-to-pelvis offset (leaning over one hip).
///     Outputs an analog weightShiftX (-1..1) plus weightShiftLeft/Right booleans.
///   - Bent over: pelvis pitch past bentOverAngleThreshold.
///   - Squat (opt-in): squatDepth + isSquatting. Two paths, see UpdateSquat.
/// Squat runs first each frame so weight shift can ignore itself during a squat.
/// </summary>
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
    private Vector3 _prevPelvisPositionForSquatGate;
    private bool _hasPrevPelvisPositionForSquatGate = false;
    private float _smoothedPelvisHorizontalSpeed = 0f;

    // calibration access
    private TorsoModuleConfiguration TorsoConfig => GetModuleConfig() as TorsoModuleConfiguration;
    private TorsoCalibrationSnapshot TorsoCalibration => CurrentCalibration as TorsoCalibrationSnapshot;

    // config values with fallbacks
    public bool IsShiftTracked => TorsoConfig?.isShiftTracked ?? false;
    public bool IsBendTracked => TorsoConfig?.isBendTracked ?? false;
    public bool IsSquatTracked => TorsoConfig?.isSquatTracked ?? false;
    // NOT scaled — these two are compared against `shiftAmount`, which UpdateWeightShiftRelative
    // has ALREADY multiplied by Sensitivity. The global knob therefore already reaches weight
    // shift through the gain; scaling here too would apply it twice (quadratically).
    public float WeightShiftThreshold => TorsoConfig?.weightShiftThreshold ?? 0.15f;
    public float NeutralZoneWidth => TorsoConfig?.neutralZoneWidth ?? 0.05f;
    // compared against the raw relative rotation / raw squat depth, so these two do scale
    public float BentOverAngleThreshold => MotionTrackingTuning.ScaleThreshold(TorsoConfig?.bentOverAngleThreshold ?? 30f);
    // NOT scaled: a ratio used to tell torso-only movement from whole-body movement
    public float WholeBodyMovementThreshold => TorsoConfig?.wholeBodyMovementThreshold ?? 3f;
    public float SquatThreshold => MotionTrackingTuning.ScaleThreshold(TorsoConfig?.squatThreshold ?? 0.12f);
    // NOT scaled: the three below are correctness guards (is the player walking? is the
    // landmark trustworthy? is a squat swallowing the shift?), not difficulty dials
    public float SquatWalkingSpeedThreshold => TorsoConfig?.squatWalkingSpeedThreshold ?? 0.5f;
    public float SquatMinLandmarkConfidence => TorsoConfig?.squatMinLandmarkConfidence ?? 0.5f;
    public float SquatSuppressesShiftAt => TorsoConfig?.squatSuppressesShiftAt ?? 0.08f;

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
        _hasPrevPelvisPositionForSquatGate = false;
        _smoothedPelvisHorizontalSpeed = 0f;
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

        // Squat runs first so weight-shift can check state.squatDepth and suppress
        // itself when the user is bending into a squat rather than shifting sideways.
        if (IsSquatTracked)
            UpdateSquat(ref state, pelvis);

        if (IsShiftTracked)
            UpdateWeightShiftRelative(ref state, pelvisMovement, spineMovement, relativeMovement);

        if (IsBendTracked)
            UpdateBentOver(ref state, relativeRotation);
    }

    // Weight shift. relativeMovement is how far the spine has moved sideways RELATIVE TO the pelvis
    // since calibration, so lean counts but moving the whole body doesn't.
    // Two things zero the shift:
    //   - isWholeBodyMovement: the spine's absolute X movement is more than WholeBodyMovementThreshold
    //     times the pelvis's (only checked once both have moved > 1 cm).
    //   - a squat deeper than SquatSuppressesShiftAt, since bending down shifts the spine too.
    // The left/right booleans latch on past ±NeutralZoneWidth and release back inside it.
    // WeightShiftThreshold only scales the analog weightShiftX.
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

        bool isSquattingNow = IsSquatTracked && state.squatDepth > SquatSuppressesShiftAt;

        float shiftAmount = relativeMovement.x * Sensitivity;

        if (isWholeBodyMovement || isSquattingNow)
        {
            shiftAmount = 0f;
            if (DebugMode && Time.frameCount % 60 == 0)
                Debug.Log($"TorsoTrackingModule: Ignoring weight shift — {(isSquattingNow ? $"squat in progress (depth={state.squatDepth:F3})" : "whole-body movement detected")}");
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

    // Squat detection has two paths, and they report squatDepth in DIFFERENT units:
    //
    //   1. Knee angle (MediaPipe / OAK-D). Uses the hip/knee/ankle world landmarks the Python sender
    //      appends to each packet. Depth = knee bend beyond the calibrated angle / 80°, clamped 0–1.
    //      It works without room calibration, which is why it's preferred for these sources.
    //   2. Joint path (Captury / Kinect, or MediaPipe without world landmarks). Depth = metres the
    //      hips have dropped toward the knees, measured against the room floor plane when there is one.
    //      This worked well on Captury and Kinect. On MediaPipe, joint positions are relative to the
    //      hips, so the hip drop isn't visible, which is why path 1 exists.
    //
    // Both paths zero the depth while walking (pelvis moving > SquatWalkingSpeedThreshold), since gait
    // bends the knees too.
    //
    // KNOWN ISSUE: path 1 checks for MediaPipeMotionTrackingManager directly, so this module
    // depends on one specific source. A cleaner design would expose the world key landmarks
    // through an optional interface on the manager.
    // KNOWN ISSUE: squatThreshold means ~0–1 on path 1 and metres on path 2, so one config value
    // can't be correct for both kinds of source.
    private void UpdateSquat(ref CapturyInputState state, Transform pelvis)
    {
        var cal = TorsoCalibration;
        bool log = DebugMode && Time.frameCount % 60 == 0;
        var mpManager = manager as MediaPipeMotionTrackingManager;

        // Walking gate: gait naturally flexes the knees enough during the
        // double-support phase to cross the squat depth threshold, so suppress
        // squat detection while the pelvis is translating across the floor
        // (squats are performed roughly in place; walking is not).
        float horizontalSpeed = 0f;
        if (_hasPrevPelvisPositionForSquatGate && Time.deltaTime > 0f)
        {
            Vector3 delta = pelvis.position - _prevPelvisPositionForSquatGate;
            horizontalSpeed = new Vector2(delta.x, delta.z).magnitude / Time.deltaTime;
        }
        _prevPelvisPositionForSquatGate = pelvis.position;
        _hasPrevPelvisPositionForSquatGate = true;
        _smoothedPelvisHorizontalSpeed = Mathf.Lerp(_smoothedPelvisHorizontalSpeed, horizontalSpeed, Time.deltaTime * 8f);
        bool isWalking = _smoothedPelvisHorizontalSpeed > SquatWalkingSpeedThreshold;

        // Priority 1: Knee angle from world landmarks — scale-invariant, works for all modes.
        // Uses the minimum of the two individual knee drops so a leg lift (one knee bends,
        // other stays straight) produces zero squat depth rather than a false positive.
        if (mpManager != null && mpManager.HasWorldKeyLandmarks && mpManager.HasWorldKeyAnkles && cal.neutralHipKneeRatio > 0f)
        {
            // Knee/ankle visibility gate: these landmarks are the first to degrade when
            // legs are cropped out of frame or occluded (e.g. standing close to the
            // camera), and a short hip-knee-ankle lever arm turns small position noise
            // into a large angle swing. Hold the last known depth rather than trust a
            // possibly-hallucinated angle when confidence is low.
            float kneeAnkleConfidence = Mathf.Min(
                Mathf.Min(mpManager.GetLandmarkConfidence(25), mpManager.GetLandmarkConfidence(26)),
                Mathf.Min(mpManager.GetLandmarkConfidence(27), mpManager.GetLandmarkConfidence(28)));
            if (kneeAnkleConfidence < SquatMinLandmarkConfidence)
            {
                if (log) Debug.Log($"[SQUAT] Knee angle — low landmark confidence (min={kneeAnkleConfidence:F2} < {SquatMinLandmarkConfidence:F2}), holding depth={_smoothedSquatDepth:F3}");
                ApplySquatState(ref state, _smoothedSquatDepth);
                return;
            }

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
            if (isWalking) angleDrop = 0f;
            float rawDepth = Mathf.Clamp01(angleDrop / 80f);
            _smoothedSquatDepth = Mathf.Lerp(_smoothedSquatDepth, rawDepth, Time.deltaTime * 8f);
            if (log) Debug.Log($"[SQUAT] Knee angle — neutral={cal.neutralHipKneeRatio:F1}° L={lAngle:F1}°(drop={lDrop:F1}) R={rAngle:F1}°(drop={rDrop:F1}) ratio={ratio:F2} walking={isWalking}(speed={_smoothedPelvisHorizontalSpeed:F2}) drop={angleDrop:F1}° depth={_smoothedSquatDepth:F3}");
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
        if (isWalking) jointDepth = 0f;
        if (log) Debug.Log($"[SQUAT] Joint path — neutral={cal.neutralHipKneeDistance:F3} current={currentDist:F3} walking={isWalking}(speed={_smoothedPelvisHorizontalSpeed:F2}) depth={jointDepth:F3} floorPlane={fp.HasValue}");
        ApplySquatState(ref state, jointDepth);
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