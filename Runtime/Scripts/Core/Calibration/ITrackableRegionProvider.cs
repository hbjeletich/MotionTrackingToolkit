using UnityEngine;

public enum TrackableRegionSource { None, Analytical, DepthFrustum, Discovered, ExternalSDK }

/// <summary>
/// Capability interface for managers that can report where the player can be reliably tracked.
/// The region may be computed analytically (camera frustum), from depth data, walked, or
/// supplied by an external SDK — consumers don't need to know which.
/// Discover via MotionTrackingOrchestrator cast.
/// </summary>
public interface ITrackableRegionProvider
{
    bool                  HasTrackableRegion { get; }
    TrackableRegionSource RegionSource       { get; }
    Vector2[]             GetTrackableRegion();  // game-space XZ polygon
}
