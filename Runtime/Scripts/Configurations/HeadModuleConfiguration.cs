using UnityEngine;

[System.Serializable]
public class HeadModuleConfiguration : ModuleConfiguration
{
    [Header("Head Position")]
    public bool isHeadPositionTracked = true;

    [Tooltip("Use relative positions from calibration vs absolute world positions")]
    public bool useRelativeHeadPosition = true;

    [Header("Head Rotation")]
    public bool isHeadRotationTracked = true;

    [Header("Direction Detection")]
    public bool isHeadDirectionEnabled = true;

    [Tooltip("Degrees of upward tilt required to trigger head up detection")]
    public float headUpThreshold = 15f;

    [Tooltip("Degrees of downward tilt required to trigger head down detection")]
    public float headDownThreshold = 15f;

    [Tooltip("Degrees of leftward rotation required to trigger head left detection")]
    public float headLeftThreshold = 20f;

    [Tooltip("Degrees of rightward rotation required to trigger head right detection")]
    public float headRightThreshold = 20f;

    [Header("Joint Names")]
    public string headJointName = "Head";
    public string neckJointName = "Neck";

    public override string[] GetRequiredJointNames()
    {
        return new string[] { headJointName, neckJointName };
    }

    public override MotionTrackingModule CreateModule(GameObject moduleParent)
    {
        GameObject obj = new GameObject("HeadTrackingModule");
        obj.transform.SetParent(moduleParent.transform);
        return obj.AddComponent<HeadTrackingModule>();
    }

    public override void ApplyJointDefaults(MotionSource source)
    {
        switch (source)
        {
            case MotionSource.Captury:
                headJointName = "Head";
                neckJointName = "Neck";
                break;

            case MotionSource.Kinect:
                headJointName = "Head";
                neckJointName = "Neck";
                break;

            case MotionSource.OakD:
            case MotionSource.MediaPipe:
                headJointName = "Head";
                neckJointName = "Neck";
                break;
        }
    }
}