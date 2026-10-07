using UnityEngine;

[System.Serializable]
public class FootModuleConfiguration : ModuleConfiguration
{
    [Header("Foot Raise")]
    [Tooltip("Track footRaised/footLowered: true when either foot is higher than the other. It does not report which foot.")]
    public bool isFootRaiseTracked = true;

    [Tooltip("Height difference (m) between the two feet that counts as a raised foot.")]
    public float footRaiseThreshold = 0.1f;

    [Header("Hip Abduction")]
    [Tooltip("Track left/right hip abduction: one foot lifted AND the feet farther apart than at calibration. Known to be weak. See the KNOWN ISSUE note in FootTrackingModule.UpdateHipAbduction.")]
    public bool isHipAbductionTracked = true;

    [Tooltip("How much farther apart (m, measured horizontally) the feet must be than at calibration to count as abduction.")]
    public float minAbductionDistance = 0.2f;

    [Tooltip("Height (m) above the floor a foot must be lifted to count toward abduction. Gait step detection treats a foot below half this height as on the ground.")]
    public float minLiftHeight = 0.05f;

    [Header("Foot Position")]
    [Tooltip("Report leftFootPosition/rightFootPosition.")]
    public bool isFootPositionTracked = true;

    [Tooltip("Use relative positions from calibration vs absolute world positions")]
    public bool useRelativeFootPosition = true;

    [Header("Walk Detection")]
    [Tooltip("Detect walking in place or across the room from spine speed (Idle → InitiatingWalk → Walking → Stopping). Adds the walk spine joint to the required joints.")]
    public bool enableWalkTracking = false;

    [Tooltip("Spine speed (m/s) that starts a walk. Speed is measured over the last 30 Unity frames, which assumes 60 fps.")]
    public float walkSpeedThreshold = 0.3f;

    [Tooltip("How long movement must continue to confirm walking")]
    public float minimumWalkDuration = 2.0f;

    [Tooltip("Speed below which walking stops")]
    public float walkStopThreshold = 0.1f;

    [Header("Gait Analysis")]
    [Tooltip("Detect individual steps (foot touches down) and compute step time, cadence, step-time asymmetry, and gait consistency.")]
    public bool enableGaitAnalysis = false;

    [Tooltip("Need this many complete cycles before analysis is reliable")]
    public int minimumCyclesForAnalysis = 3;

    [Tooltip("Filter out unrealistic step times - maximum")]
    public float maxReasonableStepTime = 2.0f;

    [Tooltip("Filter out unrealistic step times - minimum")]
    public float minReasonableStepTime = 0.3f;

    [Tooltip("Frames of position history to keep (300 = ~5 seconds at 60fps)")]
    public int positionHistoryFrames = 300;

    [Tooltip("Number of recent foot events to remember")]
    public int eventHistoryCount = 20;

    [Header("Joint Names")]
    public string leftFootJointName = "LeftFoot";
    public string rightFootJointName = "RightFoot";

    [Tooltip("Spine joint for walk speed tracking (separate from TorsoModule)")]
    public string walkTrackingSpineJointName = "Spine";

    public override string[] GetRequiredJointNames()
    {
        if (enableWalkTracking || enableGaitAnalysis)
        {
            return new string[] { leftFootJointName, rightFootJointName, walkTrackingSpineJointName };
        }
        return new string[] { leftFootJointName, rightFootJointName };
    }

    public override MotionTrackingModule CreateModule(GameObject moduleParent)
    {
        GameObject obj = new GameObject("FootTrackingModule");
        obj.transform.SetParent(moduleParent.transform);
        return obj.AddComponent<FootTrackingModule>();
    }

    // NOTE: Unity only calls OnValidate on MonoBehaviours and ScriptableObjects. This is a plain
    // [Serializable] class inside MotionTrackingConfiguration, so this never runs automatically and
    // walkStopThreshold < walkSpeedThreshold is NOT enforced. Keep them in that order by hand.
    private void OnValidate()
    {
        if (walkStopThreshold >= walkSpeedThreshold)
        {
            walkStopThreshold = walkSpeedThreshold * 0.5f;
        }
    }

    public override void ApplyJointDefaults(MotionSource source)
    {
        switch (source)
        {
            case MotionSource.Captury:
                leftFootJointName = "LeftFoot";
                rightFootJointName = "RightFoot";
                walkTrackingSpineJointName = "Spine";
                break;

            case MotionSource.Kinect:
                leftFootJointName = "AnkleLeft";
                rightFootJointName = "AnkleRight";
                walkTrackingSpineJointName = "SpineMid";
                break;

            case MotionSource.OakD:
            case MotionSource.MediaPipe:
                leftFootJointName = "LeftFoot";
                rightFootJointName = "RightFoot";
                walkTrackingSpineJointName = "Spine";
                break;
        }
    }
}