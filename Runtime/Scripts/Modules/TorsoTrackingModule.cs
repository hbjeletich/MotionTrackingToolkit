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

        public override CalibrationSnapshot Clone()
        {
            return new TorsoCalibrationSnapshot
            {
                timestamp = timestamp,
                neutralPelvisPosition = neutralPelvisPosition,
                neutralPelvisRotation = neutralPelvisRotation,
                neutralSpinePosition = neutralSpinePosition,
                neutralSpineToPelvisOffset = neutralSpineToPelvisOffset
            };
        }
    }

    #endregion

    #region Variables

    // internal states
    private bool isShiftingLeft = false;
    private bool isShiftingRight = false;
    private bool isBentOver = false;

    // calibration access
    private TorsoModuleConfiguration TorsoConfig => GetModuleConfig() as TorsoModuleConfiguration;
    private TorsoCalibrationSnapshot TorsoCalibration => CurrentCalibration as TorsoCalibrationSnapshot;

    // config values with fallbacks
    public bool IsShiftTracked => TorsoConfig?.isShiftTracked ?? false;
    public bool IsBendTracked => TorsoConfig?.isBendTracked ?? false;
    public float WeightShiftThreshold => TorsoConfig?.weightShiftThreshold ?? 0.15f;
    public float NeutralZoneWidth => TorsoConfig?.neutralZoneWidth ?? 0.05f;
    public float BentOverAngleThreshold => TorsoConfig?.bentOverAngleThreshold ?? 30f;
    public float WholeBodyMovementThreshold => TorsoConfig?.wholeBodyMovementThreshold ?? 3f;

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

        var snapshot = new TorsoCalibrationSnapshot
        {
            neutralPelvisPosition = pelvis.position,
            neutralPelvisRotation = pelvis.eulerAngles,
            neutralSpinePosition = spine.position,
            neutralSpineToPelvisOffset = spine.position - pelvis.position
        };

        Debug.Log($"TorsoTrackingModule: Captured calibration — " +
                 $"Pelvis: {snapshot.neutralPelvisPosition:F3}, Spine: {snapshot.neutralSpinePosition:F3}, " +
                 $"Offset: {snapshot.neutralSpineToPelvisOffset:F3}");

        return snapshot;
    }

    protected override void OnCalibrationApplied()
    {
        // reset internal state when calibration changes
        isShiftingLeft = false;
        isShiftingRight = false;
        isBentOver = false;
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

    #endregion
}