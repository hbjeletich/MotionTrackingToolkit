using UnityEngine;

[System.Serializable]
public class ArmModuleConfiguration : ModuleConfiguration
{
    [Header("Hand Position")]
    public bool isHandPositionTracked = true;

    [Tooltip("Use relative positions from calibration vs absolute world positions")]
    public bool useRelativeHandPosition = true;

    [Header("Hand Raise Detection")]
    public bool isHandRaiseTracked = true;

    [Tooltip("Height above shoulder required to trigger hand raised")]
    public float handRaiseThreshold = 0.3f;

    [Tooltip("Minimum height gain from neutral position")]
    public float handRaiseMinHeight = 0.1f;

    [Header("Joint Names")]
    public string leftHandJointName = "LeftHand";
    public string rightHandJointName = "RightHand";
    public string leftShoulderJointName = "LeftShoulder";
    public string rightShoulderJointName = "RightShoulder";

    public override string[] GetRequiredJointNames()
    {
        return new string[] { leftHandJointName, rightHandJointName, leftShoulderJointName, rightShoulderJointName };
    }

    public override MotionTrackingModule CreateModule(GameObject moduleParent)
    {
        GameObject obj = new GameObject("ArmTrackingModule");
        obj.transform.SetParent(moduleParent.transform);
        return obj.AddComponent<ArmTrackingModule>();
    }

    public override void ApplyJointDefaults(MotionSource source)
    {
        switch (source)
        {
            case MotionSource.Captury:
                leftHandJointName = "LeftHand";
                rightHandJointName = "RightHand";
                leftShoulderJointName = "LeftShoulder";
                rightShoulderJointName = "RightShoulder";
                break;

            case MotionSource.Kinect:
                leftHandJointName = "HandLeft";
                rightHandJointName = "HandRight";
                leftShoulderJointName = "ShoulderLeft";
                rightShoulderJointName = "ShoulderRight";
                break;

            case MotionSource.MediaPipe:
                leftHandJointName = "LeftHand";
                rightHandJointName = "RightHand";
                leftShoulderJointName = "LeftShoulder";
                rightShoulderJointName = "RightShoulder";
                break;
        }
    }
}