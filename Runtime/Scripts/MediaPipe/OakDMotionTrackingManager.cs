/// <summary>
/// IMotionTrackingManager for OAK-D depth-based MediaPipe pose landmarks.
///
/// Identical wire protocol, joint mapping, and calibration/room-scale behavior to
/// MediaPipeMotionTrackingManager — OAK-D only differs in how the Python sender
/// produces the landmarks (real stereo depth back-projection vs. MediaPipe's
/// estimated world-landmark scale). See mediapipe_sender/oak_d_sender.py.
///
/// Scene setup: identical to MediaPipeMotionTrackingManager — GameObject with
/// MediaPipeInput + OakDMotionTrackingManager, running the OAK-D sender exe
/// (MediaPipeProcessManager picks the right sender/args for the active source).
/// </summary>
public class OakDMotionTrackingManager : MediaPipeMotionTrackingManager
{
    public override MotionSource Source => MotionSource.OakD;
}
