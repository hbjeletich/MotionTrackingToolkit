// Hook invoked by a manager when a skeleton/body is ready to calibrate,
// before the manager's own live auto-calibration runs.
// Subscribe to supply saved calibration from a previous session.
// Return true to indicate calibration was provided — the manager skips auto-calibration.
// Only one subscriber is expected (the CalibrationGuard singleton).
public static class CalibrationHooks
{
    public static System.Func<ICalibratableTrackingManager, bool> OnSkeletonReadyForCalibration;
}
