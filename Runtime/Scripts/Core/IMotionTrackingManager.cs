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
}
