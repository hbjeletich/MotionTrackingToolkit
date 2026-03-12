using UnityEngine;

[System.Serializable]
public class BalanceModuleConfiguration : ModuleConfiguration
{
    [Header("Tracking Features")]
    [Tooltip("Track center of mass position")]
    public bool isCoMTracked = true;

    [Tooltip("Detect and measure body sway during balance")]
    public bool isSwayTracked = true;

    [Tooltip("Track balance stability and detect balance loss/regain")]
    public bool isStabilityTracked = true;

    [Header("Thresholds")]
    public float swayThreshold = 0.1f;

    [Tooltip("CoM velocity threshold (m/s) — below this is considered stable")]
    public float stabilityThreshold = 0.05f;

    [Tooltip("Frames of CoM history to keep (180 = ~3 seconds at 60fps)")]
    public int comHistoryFrames = 180;

    [Tooltip("Minimum foot lift height for contact detection (shared with FootModule)")]
    public float minLiftHeight = 0.05f;

    [Header("Joint Names")]
    public string trunkJointName = "Spine1";
    public string leftForeArmJointName = "LeftForeArm";
    public string rightForeArmJointName = "RightForeArm";
    public string leftLegJointName = "LeftLeg";
    public string rightLegJointName = "RightLeg";
    public string leftToeBaseJointName = "LeftToeBase";
    public string rightToeBaseJointName = "RightToeBase";

    public override string[] GetRequiredJointNames()
    {
        return new string[]
        {
            trunkJointName, leftForeArmJointName, rightForeArmJointName,
            leftLegJointName, rightLegJointName,
            leftToeBaseJointName, rightToeBaseJointName
        };
    }

    public override MotionTrackingModule CreateModule(GameObject moduleParent)
    {
        GameObject obj = new GameObject("BalanceTrackingModule");
        obj.transform.SetParent(moduleParent.transform);
        return obj.AddComponent<BalanceTrackingModule>();
    }

    public override void ApplyJointDefaults(MotionSource source)
    {
        switch (source)
        {
            case MotionSource.Captury:
                trunkJointName = "Spine1";
                leftForeArmJointName = "LeftForeArm";
                rightForeArmJointName = "RightForeArm";
                leftLegJointName = "LeftLeg";
                rightLegJointName = "RightLeg";
                leftToeBaseJointName = "LeftToeBase";
                rightToeBaseJointName = "RightToeBase";
                break;

            case MotionSource.Kinect:
                trunkJointName = "SpineShoulder";
                leftForeArmJointName = "ElbowLeft";
                rightForeArmJointName = "ElbowRight";
                leftLegJointName = "KneeLeft";
                rightLegJointName = "KneeRight";
                leftToeBaseJointName = "FootLeft";
                rightToeBaseJointName = "FootRight";
                break;
        }
    }
}