using UnityEngine;

/// <summary>
/// Optional capability for managers whose module-facing joint Transforms are NOT in the same
/// coordinate space as their room frame.
///
/// MediaPipe/OAK-D is the case this exists for: modules get hip-relative joints (the manager
/// subtracts the hip anchor every frame), while the room calibration's floor is stored in the
/// sender's room/camera frame. Measuring a hip-relative ankle against that raw floor gives
/// "camera height minus hip height", not foot height. A manager that implements this hands
/// modules the floor already re-expressed in joint space.
///
/// Found by casting the module's manager (see MotionTrackingModule.GetFloorPlane).
/// </summary>
public interface ISkeletonFloorProvider
{
    /// <summary>
    /// The floor plane in the same world space as the joint Transforms this manager gives
    /// modules, valid for the current frame. False when there's no floor to report (no room
    /// frame, or no absolute position this frame). Callers then fall back to the ground height
    /// captured at calibration.
    /// </summary>
    bool TryGetSkeletonFloorPlane(out Plane floor);
}
