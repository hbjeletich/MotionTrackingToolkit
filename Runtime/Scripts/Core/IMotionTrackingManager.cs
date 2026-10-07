using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The contract every motion source implements (Captury, Kinect, MediaPipe, OAK-D) and that
/// MotionTrackingOrchestrator forwards. Modules only ever talk to the source through this, which is
/// what keeps them source-agnostic. Games usually read the CapturyInput device instead, and only use
/// this for calibration, config swaps, or room position.
///
/// Optional capabilities are separate interfaces, found by casting: ICalibratableTrackingManager,
/// IRoomFrameSource, ITrackableRegionProvider, IBoundaryWalkable.
/// </summary>
public interface IMotionTrackingManager
{
    /// <summary>The active configuration (modules, thresholds, joint names).</summary>
    MotionTrackingConfiguration Config { get; }
    /// <summary>A joint Transform by the name the config uses. Names differ per source; see JointNameSets.</summary>
    Transform GetJointByName(string jointName);
    MotionSource Source { get; }
    /// <summary>Swap to another configuration. Modules are destroyed and rebuilt. If the system was
    /// already calibrated, a NEW live calibration runs after the config's calibrationDelay; the
    /// previous calibration isn't carried over.</summary>
    void LoadConfiguration(MotionTrackingConfiguration config);
    /// <summary>Take a new neutral-pose calibration now (after the config's calibration delay).</summary>
    void Recalibrate();
    /// <summary>Save every module's calibration to disk under this name (CalibrationStore).</summary>
    void SaveCalibration(string calibrationName);
    /// <summary>Load a calibration saved with SaveCalibration. Warns if it was captured on another source.</summary>
    bool LoadCalibration(string calibrationName);

    // ── Room scale ──────────────────────────────────────────────────────────
    // KNOWN ISSUE: room calibration is unfinished (see IBoundaryWalkable). Captury returns stubs.
    bool SupportsRoomScale { get; }
    /// <summary>The player's hip position in game space, mapped through the room calibration.</summary>
    bool TryGetRoomPosition(out Vector3 gamePosition);
    bool HasRoomBounds { get; }
    Vector3[] GetRoomBoundary();
    float RoomMinTrackingDistance { get; }
}
