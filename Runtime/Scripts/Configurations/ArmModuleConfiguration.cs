using UnityEngine;

[System.Serializable]
public class ArmModuleConfiguration : ModuleConfiguration
{
    [Header("Hand Position")]
    [Tooltip("Report leftHandPosition/rightHandPosition.")]
    public bool isHandPositionTracked = true;

    [Tooltip("Use relative positions from calibration vs absolute world positions")]
    public bool useRelativeHandPosition = true;

    [Header("Hand Raise Detection")]
    [Tooltip("Track leftHandRaised/rightHandRaised. Both conditions below must be true.")]
    public bool isHandRaiseTracked = true;

    [Tooltip("Height (m) the hand must be above its own shoulder.")]
    public float handRaiseThreshold = 0.3f;

    [Tooltip("How much higher (m) the hand must be than it was at calibration. Stops a high resting hand from counting.")]
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

            case MotionSource.OakD:
            case MotionSource.MediaPipe:
                leftHandJointName = "LeftHand";
                rightHandJointName = "RightHand";
                leftShoulderJointName = "LeftShoulder";
                rightShoulderJointName = "RightShoulder";
                break;
        }
    }
}