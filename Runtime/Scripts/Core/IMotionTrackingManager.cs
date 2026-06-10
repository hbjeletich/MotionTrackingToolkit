using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public interface IMotionTrackingManager
{
    MotionTrackingConfiguration Config { get; }
    Transform GetJointByName(string jointName);
    MotionSource Source { get; }
    void LoadConfiguration(MotionTrackingConfiguration config);
    void Recalibrate();
    void SaveCalibration(string calibrationName);
    bool LoadCalibration(string calibrationName);
    bool SupportsRoomScale { get; }
    bool TryGetRoomPosition(out Vector3 gamePosition);
    bool HasRoomBounds { get; }
    Vector3[] GetRoomBoundary();
    float RoomMinTrackingDistance { get; }
}
