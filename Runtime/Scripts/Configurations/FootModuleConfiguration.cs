using UnityEngine;

[System.Serializable]
public class FootModuleConfiguration : ModuleConfiguration
{
    [Header("Foot Raise")]
    public bool isFootRaiseTracked = true;

    [Tooltip("Minimum height difference between feet to trigger foot raise")]
    public float footRaiseThreshold = 0.1f;

    [Header("Hip Abduction")]
    public bool isHipAbductionTracked = true;

    [Tooltip("Additional distance feet must spread beyond normal stance")]
    public float minAbductionDistance = 0.2f;

    [Tooltip("Minimum foot lift height required for abduction detection")]
    public float minLiftHeight = 0.05f;

    [Header("Foot Position")]
    public bool isFootPositionTracked = true;

    [Tooltip("Use relative positions from calibration vs absolute world positions")]
    public bool useRelativeFootPosition = true;

    [Header("Walk Detection")]
    public bool enableWalkTracking = false;

    [Tooltip("Minimum movement speed (m/s) to consider walking")]
    public float walkSpeedThreshold = 0.3f;

    [Tooltip("How long movement must continue to confirm walking")]
    public float minimumWalkDuration = 2.0f;

    [Tooltip("Speed below which walking stops")]
    public float walkStopThreshold = 0.1f;

    [Header("Gait Analysis")]
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

            case MotionSource.MediaPipe:
                leftFootJointName = "LeftFoot";
                rightFootJointName = "RightFoot";
                walkTrackingSpineJointName = "Spine";
                break;
        }
    }
}