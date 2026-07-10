/// <summary>
/// Canonical joint identity plus per-tracking-source lookup names, shared by anything that needs
/// to walk the whole skeleton across sources (MotionRecorder, SkeletonPanel, ...).
/// IMotionTrackingManager.GetJointByName does not use the same argument strings across sources
/// (Captury/MediaPipe use BVH-style names, Kinect uses its own), so consumers must translate
/// through this table rather than picking one name list and querying every source with it.
/// Single source of truth — do not duplicate these arrays elsewhere.
/// </summary>
public static class JointNameSets
{
    // Canonical joint identity — same across all sources. Parallel-indexed with
    // BvhLookupNames/KinectLookupNames below.
    public static readonly string[] CanonicalNames = {
        "Head", "Neck", "SpineShoulder", "SpineMid", "SpineBase",
        "LeftShoulder",  "LeftElbow",  "LeftWrist",
        "RightShoulder", "RightElbow", "RightWrist",
        "LeftHip",  "LeftKnee",  "LeftAnkle",  "LeftFoot",
        "RightHip", "RightKnee", "RightAnkle", "RightFoot",
    };

    // BVH-style names used by Captury and MediaPipe (derived from per-module ApplyJointDefaults).
    // Note: BVH "LeftShoulder"/"RightShoulder" are the clavicles; "LeftArm"/"RightArm" are the
    // actual glenohumeral joints that correspond to the canonical "LeftShoulder"/"RightShoulder".
    public static readonly string[] BvhLookupNames = {
        "Head", "Neck", "Spine4", "Spine", "Hips",
        "LeftArm",  "LeftForeArm",  "LeftHand",
        "RightArm", "RightForeArm", "RightHand",
        "LeftUpLeg",  "LeftLeg",  "LeftFoot",  "LeftToeBase",
        "RightUpLeg", "RightLeg", "RightFoot", "RightToeBase",
    };

    // Kinect v2 names (from KinectJointMapper).
    public static readonly string[] KinectLookupNames = {
        "Head", "Neck", "SpineShoulder", "SpineMid", "SpineBase",
        "ShoulderLeft",  "ElbowLeft",  "WristLeft",
        "ShoulderRight", "ElbowRight", "WristRight",
        "HipLeft",  "KneeLeft",  "AnkleLeft",  "FootLeft",
        "HipRight", "KneeRight", "AnkleRight", "FootRight",
    };

    public static string[] GetLookupNames(MotionSource source) =>
        source == MotionSource.Kinect ? KinectLookupNames : BvhLookupNames;
}
