using System;

public enum BundleProvenance { Captured, Loaded, ExternalSDK }

/// <summary>
/// The unit of persistence for room calibration: a frame + boundary + metadata.
/// Serialized to JSON via RoomCalibrationStore.SaveBundle / LoadBundle.
/// </summary>
[Serializable]
public class RoomCalibrationBundle
{
    public string           name;
    public MotionSource     source;
    public string           sourceFingerprint; // e.g. OAK-D serial; for camera-moved detection
    public string           capturedAtIso;     // ISO 8601 timestamp (DateTime serializes poorly in JsonUtility)
    public RoomFrame        frame;
    public RoomBoundary     boundary;
    public BundleProvenance provenance;
}
