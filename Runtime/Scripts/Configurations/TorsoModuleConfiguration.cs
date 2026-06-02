using UnityEngine;

[System.Serializable]
public class TorsoModuleConfiguration : ModuleConfiguration
{
    [Header("Weight Shift")]
    public bool isShiftTracked = true;

    [Tooltip("How far center of mass must shift to trigger weight shift")]
    public float weightShiftThreshold = 0.15f;

    [Tooltip("Width of neutral zone to prevent fluttering")]
    public float neutralZoneWidth = 0.05f;

    [Tooltip("Ratio threshold to ignore whole-body movement vs torso-only movement")]
    public float wholeBodyMovementThreshold = 3f;

    [Header("Bend Detection")]
    public bool isBendTracked = true;

    [Tooltip("Degrees of forward bend to trigger bent over detection")]
    public float bentOverAngleThreshold = 30f;

    [Header("Joint Names")]
    [Tooltip("Name of the pelvis/hips joint in your skeleton")]
    public string pelvisJointName = "Hips";

    [Tooltip("Name of the spine joint used for torso tracking")]
    public string spineJointName = "Spine4";

    public override string[] GetRequiredJointNames()
    {
        return new string[] { pelvisJointName, spineJointName };
    }

    public override MotionTrackingModule CreateModule(GameObject moduleParent)
    {
        GameObject obj = new GameObject("TorsoTrackingModule");
        obj.transform.SetParent(moduleParent.transform);
        return obj.AddComponent<TorsoTrackingModule>();
    }

    public override void ApplyJointDefaults(MotionSource source)
    {
        switch (source)
        {
            case MotionSource.Captury:
                pelvisJointName = "Hips";
                spineJointName = "Spine4";
                break;

            case MotionSource.Kinect:
                pelvisJointName = "SpineBase";
                spineJointName = "SpineShoulder";
                break;

            case MotionSource.MediaPipe:
                pelvisJointName = "Hips";
                spineJointName = "Spine4";
                break;
        }
    }
}