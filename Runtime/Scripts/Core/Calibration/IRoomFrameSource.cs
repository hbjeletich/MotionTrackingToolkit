using System;
using UnityEngine;

public struct FrameCapturePolicy
{
    public float dwellSeconds;       // how long to hold the pose before sampling
    public int   medianSampleCount;  // number of frames to median over at capture moment

    public static FrameCapturePolicy Default => new FrameCapturePolicy
    {
        dwellSeconds      = 3f,
        medianSampleCount = 30,
    };
}

/// <summary>
/// Capability interface for managers that can establish a room coordinate frame.
/// Implement on a manager to opt in; discover via MotionTrackingOrchestrator cast.
/// </summary>
public interface IRoomFrameSource
{
    bool      HasRoomFrame { get; }
    RoomFrame CurrentFrame { get; }
    bool      TryGetRoomPosition(out Vector3 gamePosition);
    void      StartFrameCapture(FrameCapturePolicy policy);
    event Action<RoomFrame> OnFrameCaptured;
}
