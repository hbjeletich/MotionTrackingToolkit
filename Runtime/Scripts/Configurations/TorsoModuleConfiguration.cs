using UnityEngine;

[System.Serializable]
public class TorsoModuleConfiguration : ModuleConfiguration
{
    [Header("Weight Shift")]
    [Tooltip("Track left/right weight shift: the sideways change in the spine-to-pelvis offset since calibration (leaning the upper body over one hip).")]
    public bool isShiftTracked = true;

    [Tooltip("Sideways spine-vs-pelvis movement (m, after sensitivity) that maps to a full ±1 on the analog weightShiftX value. Does NOT gate the left/right booleans; neutralZoneWidth does.")]
    public float weightShiftThreshold = 0.15f;

    [Tooltip("Sideways spine-vs-pelvis movement (m, after sensitivity) past which weightShiftLeft/Right turn on, and inside which they turn back off. Raise it if shifts trigger too easily.")]
    public float neutralZoneWidth = 0.05f;

    [Tooltip("Shift is ignored when the spine has moved sideways more than this many times as far as the pelvis since calibration (compares absolute X movement of each joint).")]
    public float wholeBodyMovementThreshold = 3f;

    [Tooltip("Squat depth (0-1) above which weight-shift detection is suppressed, so bending into a squat isn't misread as a lateral weight shift.")]
    public float squatSuppressesShiftAt = 0.08f;

    [Header("Bend Detection")]
    [Tooltip("Track bending forward (isBentOver / isUpright) from pelvis pitch.")]
    public bool isBendTracked = true;

    [Tooltip("Degrees of pelvis pitch (X rotation) away from the calibrated pose that counts as bent over.")]
    public float bentOverAngleThreshold = 30f;

    [Header("Squat Detection")]
    [Tooltip("Track squats (squatDepth / isSquatting). Adds the two knee joints to this module's required joints. MediaPipe/OAK-D use the knee angle from world landmarks; other sources use how far the hips drop toward the knees.")]
    public bool isSquatTracked = false;

    [Tooltip("squatDepth above which isSquatting turns on. Units depend on the source. MediaPipe/OAK-D (knee angle): 0–1, where 1 = 80° more knee bend than at calibration, so 0.15 ≈ 12°. Captury/Kinect (joint path): metres the hips have dropped toward the knees, so 0.15 = 15 cm.")]
    public float squatThreshold = 0.15f;

    [Tooltip("Horizontal pelvis speed (m/s) above which squat detection is suppressed — filters out knee flexion from walking gait rather than a stationary squat.")]
    public float squatWalkingSpeedThreshold = 0.5f;

    [Tooltip("Minimum MediaPipe visibility/confidence (0-1) required on the knee and ankle landmarks before trusting the knee-angle squat signal. Guards against unreliable estimates when legs are cropped or occluded, e.g. standing close to the camera.")]
    public float squatMinLandmarkConfidence = 0.5f;

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

            case MotionSource.OakD:
            case MotionSource.MediaPipe:
                pelvisJointName = "Hips";
                spineJointName = "Spine4";
                leftKneeJointName = "LeftLeg";
                rightKneeJointName = "RightLeg";
                break;
        }
    }
}