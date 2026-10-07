using UnityEngine;

/// <summary>
/// Implemented by motion tracking managers that support room boundary calibration.
/// </summary>
// KNOWN ISSUE: room calibration is unfinished. Two generations of the API exist side by side:
//   - This interface (the first version): StartRoomCalibration + a boundary walk around the room
//     perimeter, stored as a RoomCalibration.
//   - The second version: IRoomFrameSource (RoomFrame: origin/facing/floor plane),
//     ITrackableRegionProvider (where tracking is reliable, e.g. computed from the OAK-D camera
//     frustum via FrustumProjector), RoomBoundary (a play rectangle inside that region), and
//     BoundaryProximityService (Safe/Warning/Danger events).
// No game code calls the IBoundaryWalkable methods anymore. Games use the second version plus
// TryGetRoomPosition / GetRoomBoundary on IMotionTrackingManager. Managers still implement both. A
// "corners instead of perimeter" calibration was started but never properly tested.
// Development stopped when room-scale gameplay (walking around the room) was set aside for
// time, so treat this whole area as a prototype.
public interface IBoundaryWalkable
{
    bool HasRoomCalibration { get; }
    bool HasRoomBounds { get; }
    void StartRoomCalibration();
    bool MergeSavedBoundary(string calibrationName);
    void SaveRoomCalibration(string name);
    bool TryGetRoomPosition(out Vector3 gamePosition);
    void SetBoundaryPoints(Vector3[] gameSpacePoints);
}
