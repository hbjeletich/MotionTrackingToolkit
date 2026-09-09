// defines which motion capture system provides skeleton data.
// used by MotionTrackingConfiguration to auto-populate joint names
// and by the editor to show/hide joint name fields.
//
// to add a new source:
//   1. add an entry here
//   2. add a case in each ModuleConfiguration.ApplyJointDefaults()
//   3. create a manager that implements IMotionTrackingManager

public enum MotionSource
{
    Custom,       // user sets joint names manually (any skeleton)
    Captury,      // CapturyLive default skeleton naming
    Kinect,       // Kinect via Windows.Kinect plugin
    MediaPipe,    // MediaPipe pose landmarks via Python UDP sender (webcam)
    OakD          // MediaPipe pose landmarks lifted through OAK-D stereo depth — same wire
                  // protocol and joint names as MediaPipe, see OakDMotionTrackingManager
}