using UnityEngine;


// Base class for calibration data snapshots
// Each module extends this with its own calibration-specific data
// snapshots can be stored, cloned, swapped, and serialized

[System.Serializable]
public abstract class CalibrationSnapshot
{
    public float timestamp;
    public abstract CalibrationSnapshot Clone();
}