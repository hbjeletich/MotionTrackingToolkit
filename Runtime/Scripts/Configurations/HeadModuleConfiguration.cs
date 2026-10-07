using UnityEngine;

[System.Serializable]
public class HeadModuleConfiguration : ModuleConfiguration
{
    [Header("Head Position")]
    [Tooltip("Report headPosition: the head-to-neck offset relative to calibration (or absolute, see below).")]
    public bool isHeadPositionTracked = true;

    [Tooltip("Use relative positions from calibration vs absolute world positions")]
    public bool useRelativeHeadPosition = true;

    [Header("Head Rotation")]
    [Tooltip("Report headRotation: head rotation relative to the neck, minus the calibrated offset (degrees).")]
    public bool isHeadRotationTracked = true;

    [Header("Direction Detection")]
    [Tooltip("Track headUp/Down/Left/Right. Up/down use the relative Z rotation (roll axis on these skeletons), and left/right use the relative Y rotation (yaw).")]
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