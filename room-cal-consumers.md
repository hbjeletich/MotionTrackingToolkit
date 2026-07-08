# Room Calibration API — Consumer Inventory

Generated for Step 0.1. All files that reference the existing room calibration API, grouped by file. This is the "do not break" list for the refactor.

---

## CapturyUnityToolkit

### `Runtime/Scripts/Core/Calibration/IBoundaryWalkable.cs`
*Defines the interface — the source of truth for the old API.*

| Symbol | Line |
|---|---|
| `IBoundaryWalkable` (declaration) | 6 |
| `HasRoomCalibration` | 8 |
| `HasRoomBounds` | 9 |
| `StartRoomCalibration` | 10 |
| `MergeSavedBoundary` | 11 |
| `SaveRoomCalibration` | 12 |
| `TryGetRoomPosition` | 13 |
| `SetBoundaryPoints` | 14 |

---

### `Runtime/Scripts/Core/IMotionTrackingManager.cs`
*Interface — room-related members live here alongside pose data.*

| Symbol | Line |
|---|---|
| `TryGetRoomPosition` | 15 |
| `HasRoomBounds` | 16 |
| `GetRoomBoundary` | 17 |
| `RoomMinTrackingDistance` | 18 |

---

### `Runtime/Scripts/Core/MotionTrackingOrchestrator.cs`
*Facade — forwards to ActiveManager.*

| Symbol | Line |
|---|---|
| `TryGetRoomPosition` | 77, 80 |
| `HasRoomBounds` | 83 |
| `GetRoomBoundary` | 84 |
| `RoomMinTrackingDistance` | 85 |

---

### `Runtime/Scripts/Core/SkeletonMotionTrackingContext.cs`
*Secondary forwarding context (Captury multiplayer path).*

| Symbol | Line |
|---|---|
| `TryGetRoomPosition` | 47, 49 |
| `HasRoomBounds` | 54 |
| `GetRoomBoundary` | 56 |
| `RoomMinTrackingDistance` | 58 |

---

### `Runtime/Scripts/MediaPipe/MediaPipeMotionTrackingManager.cs`
*Implements `IBoundaryWalkable` and `IMotionTrackingManager`. Primary target of Phase 2–3.*

| Symbol | Line |
|---|---|
| `IBoundaryWalkable` (implements) | 22 |
| `defaultRoomCalibrationName` field | 52 |
| `activeRoomCalibration` field | 150 |
| `activeRoomCalibrationCoroutine` field | 151 |
| `HasRoomBounds` | 172 |
| `GetRoomBoundary` | 173–174 |
| `RoomMinTrackingDistance` | 175–177 |
| `HasRoomCalibration` | 198 |
| `StartRoomCalibration` | 824 |
| `SetBoundaryPoints` | 833–836 |
| `MergeSavedBoundary` | 849 |
| `TryGetRoomPosition` | 904 |

---

### `Runtime/Scripts/Kinect/KinectMotionTrackingManager.cs`
*Also implements `IBoundaryWalkable`. Phase 2.3 adapter target.*

| Symbol | Line |
|---|---|
| `IBoundaryWalkable` (implements) | 12 |
| `HasRoomCalibration` | 803 |
| `TryGetRoomPosition` | 805 |
| `HasRoomBounds` | 814 |
| `GetRoomBoundary` | 815 |
| `RoomMinTrackingDistance` | 817 |
| `StartRoomCalibration` | 866 |
| `SetBoundaryPoints` | 927–930 |
| `MergeSavedBoundary` | 941 |

---

### `Runtime/Scripts/Captury/MotionTrackingManager.cs`
*Stub — returns false/empty for all room methods.*

| Symbol | Line |
|---|---|
| `TryGetRoomPosition` (stub) | 43 |
| `HasRoomBounds` (stub) | 44 |
| `GetRoomBoundary` (stub) | 45 |
| `RoomMinTrackingDistance` (stub) | 46 |

---

### `Runtime/Scripts/Captury/MultiplayerMotionTrackingManager.cs`
*Stub — returns false/empty for all room methods.*

| Symbol | Line |
|---|---|
| `TryGetRoomPosition` (stub) | 821 |
| `HasRoomBounds` (stub) | 827 |
| `GetRoomBoundary` (stub) | 829 |
| `RoomMinTrackingDistance` (stub) | 831 |

---

## G4G-SP25 (game project)

### `Assets/Hub/Calibration/Scripts/RoomBoundaryFlowController.cs`
*Core consumer — the flow being replaced by `ConsumerRoomSetup` in Phase 5. To be marked `[Obsolete]` in Phase 8.1.*

| Symbol | Line |
|---|---|
| `IBoundaryWalkable` (field type) | 40 |
| `IBoundaryWalkable` (FindFirstObjectByType cast) | 44–45 |
| `StartRoomCalibration` | 60 |
| `HasRoomCalibration` | 61 |
| `MergeSavedBoundary` | 64 |
| `TryGetRoomPosition` | 87, 99, 135 |
| `SetBoundaryPoints` | 150 |
| `SaveRoomCalibration` | 151 |

---

### `Assets/Hub/Calibration/Scripts/BoundaryProximityIndicator.cs`
*Uses boundary data for proximity UI — partially superseded by `BoundaryProximityService` in Phase 7.*

| Symbol | Line |
|---|---|
| `HasRoomBounds` + `TryGetRoomPosition` | 52 |
| `HasRoomCalibration` | 69 |
| `GetRoomBoundary` | 86 |

---

### `Assets/Hub/Calibration/Scripts/RoomBoundarySceneAdvance.cs`
*Thin helper that listens for `RoomBoundaryFlowController` completion to advance scenes. To be deleted in Phase 8.3.*

| Symbol | Line |
|---|---|
| `RoomBoundaryFlowController` (GetComponent) | 9, 20 |

---

### `Assets/Consellation/AssetsConstellation/Scripts/PlayerController.cs`
*Production gameplay consumer — drives player position from room position.*

| Symbol | Line |
|---|---|
| `HasRoomBounds` | 146 |
| `GetRoomBoundary` | 148 |
| `TryGetRoomPosition` | 263 |

---

### `Assets/Consellation/AssetsConstellation/Scripts/StarStepperRoomCalibrationTest.cs`
*Debug/test harness — calls save/load/boundary via reflection.*

| Symbol | Line |
|---|---|
| `TryGetRoomPosition` | 56 |
| `HasRoomBounds` | 64, 120 |
| `GetRoomBoundary` | 65, 79, 115 |
| `RoomMinTrackingDistance` | 66, 122 |
| `SaveRoomCalibration` (via reflection) | 49 |
| `LoadRoomCalibration` (via reflection) | 72–73 |

---

### `Assets/Shared/Scripts/TrackingMonitor.cs`
*Present in workspace. Uses `MotionTrackingOrchestrator` but does not call room calibration symbols directly — only orchestrator-level properties. To be marked `[Obsolete]` in Phase 8.2.*

---

## Summary

| What | Count |
|---|---|
| Files that will need new interface wiring (Phase 2) | 2 (MediaPipe, Kinect managers) |
| Files that forward old API (no changes needed) | 4 (Orchestrator, SkeletonContext, 2× Captury stubs) |
| Game-side consumers that must keep working | 3 (PlayerController, BoundaryProximityIndicator, StarStepperTest) |
| Files to be deprecated/deleted (Phase 8) | 3 (RoomBoundaryFlowController, RoomBoundarySceneAdvance, TrackingMonitor) |
