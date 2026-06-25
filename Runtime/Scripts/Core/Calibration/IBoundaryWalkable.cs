using UnityEngine;

/// <summary>
/// Implemented by motion tracking managers that support room boundary calibration.
/// </summary>
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
