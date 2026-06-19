// Capability interface for managers that support module-level calibration inspection.
// Implemented by MotionTrackingManager (Captury) and KinectMotionTrackingManager.
// Kept separate from IMotionTrackingManager so external systems (e.g. CalibrationGuard)
// can work with calibration state without coupling scene logic to tracking internals.
public interface ICalibratableTrackingManager
{
    bool IsSystemCalibrated { get; }
    bool IsCalibrating { get; }
    MotionTrackingConfiguration Config { get; }
    MotionSource Source { get; }
    T GetModule<T>() where T : MotionTrackingModule;
}
