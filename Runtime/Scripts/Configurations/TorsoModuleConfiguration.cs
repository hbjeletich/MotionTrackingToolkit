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

    [Header("Squat Detection")]
    public bool isSquatTracked = false;

    [Tooltip("Normalized squat depth (0–1) that triggers isSquatting. 0.25 = roughly a quarter squat.")]
    public float squatThreshold = 0.15f;

    [Header("Joint Names")]
    [Tooltip("Name of the pelvis/hips joint in your skeleton")]
    public string pelvisJointName = "Hips";

    [Tooltip("Name of the spine joint used for torso tracking")]
    public string spineJointName = "Spine4";

    [Tooltip("Name of the left knee joint (only used when isSquatTracked is true)")]
    public string leftKneeJointName = "LeftLeg";

    [Tooltip("Name of the right knee joint (only used when isSquatTracked is true)")]
    public string rightKneeJointName = "RightLeg";

    public override string[] GetRequiredJointNames()
    {
        if (isSquatTracked)
            return new string[] { pelvisJointName, spineJointName, leftKneeJointName, rightKneeJointName };
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
                leftKneeJointName = "LeftLeg";
                rightKneeJointName = "RightLeg";
                break;

            case MotionSource.Kinect:
                pelvisJointName = "SpineBase";
                spineJointName = "SpineShoulder";
                leftKneeJointName = "KneeLeft";
                rightKneeJointName = "KneeRight";
                break;

            case MotionSource.MediaPipe:
                pelvisJointName = "Hips";
                spineJointName = "Spine4";
                leftKneeJointName = "LeftLeg";
                rightKneeJointName = "RightLeg";
                break;
        }
    }
}