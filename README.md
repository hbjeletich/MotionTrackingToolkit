# Motion Tracking Toolkit

Unity package for motion capture tracking using Unity's Input System. Provides modular tracking for torso, feet, arms, and head with walk detection, gait analysis, squat detection, and balance tracking. Supports three interchangeable motion sources — **Captury**, **Kinect**, and **MediaPipe** — behind a single `MotionTrackingOrchestrator` facade, plus room-scale calibration and motion recording with C3D export.

---

## Features

- **Modular Design** - Enable/disable tracking modules independently
- **Multi-Source Tracking** - Swap between Captury, Kinect, and MediaPipe at edit time or runtime via `MotionTrackingOrchestrator`, without changing any downstream code
- **Input System Integration** - Access tracking data through Unity's Input System
- **Torso Tracking** - Weight shift detection, bent over detection
- **Squat Detection** - Normalized squat depth, walking-gait suppression, landmark-confidence gating (MediaPipe)
- **Foot Tracking** - Foot raise, hip abduction, position tracking
- **Walk Detection** - Speed, cadence, walk state (idle/walking/stopping)
- **Gait Analysis** - Step timing, asymmetry, consistency metrics
- **Arm Tracking** - Hand position and raise detection
- **Head Tracking** - Position, rotation, nod/shake gesture detection
- **Balance Tracking** - Center of mass position and velocity, lateral sway, anterior/posterior sway
- **Room Calibration** - Room-scale position mapping, boundary walking, and saved calibration profiles shared across sources
- **Motion Recording** - Records full-fidelity JSON (including biomechanical input state) and natively writes `.c3d` files for biomechanics tooling
- **Configurable** - ScriptableObject-based configuration system
- **Multiplayer Support** - Supports multiple captury skeletons with instanced input action assets

---

## Installation

### Prerequisites

This package includes the **Captury Unity Plugin** (MIT License) in `/Runtime/ThirdParty/Captury/` and the **Unity Input System**.

If you plan to use the Kinect or MediaPipe sources instead of (or alongside) Captury, see [Multi-Source Tracking](#multi-source-tracking) for their additional dependencies.

### Install via Package Manager

1. Open Unity Package Manager: `Window → Package Manager`
2. Click `+` → `Add package from git URL`
3. Enter: `https://github.com/hbjeletich/MotionTrackingToolkit.git`

### Install via manifest.json

Add this line to your `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.hbjeletich.capturytoolkit": "https://github.com/hbjeletich/MotionTrackingToolkit.git"
  }
}
```

---

## Quick Start

### 1. Scene Setup

Use the BaseScene for fastest setup for singleplayer. 

For existing scenes or starting from scratch, add these components to a GameObject in your scene:

| Component | Source | Purpose |
|-----------|--------|---------|
| `CapturyNetworkPlugin` | Captury Plugin | Connects to CapturyLive |
| `CapturyInputManager` | This Package | Registers input device |
| `MotionTrackingManager` | This Package | Main tracking manager |

If you're doing multiplayer, the components are slightly different:

| Component | Source | Purpose |
|-----------|--------|---------|
| `CapturyNetworkPlugin` | Captury Plugin | Connects to CapturyLive |
| `MultiplayerTrackingManager` | This Package | Main tracking manager, handles input device registration |

> If you need to support Kinect and/or MediaPipe alongside (or instead of) Captury, use `MotionTrackingOrchestrator` in place of `MotionTrackingManager` — see [Multi-Source Tracking](#multi-source-tracking). The rest of this Quick Start assumes Captury only.

### 2. Configure Captury Connection

On the `CapturyNetworkPlugin` component:
- Set **Host** to the IP address where CapturyLive is running
- Set **Port** to `2101` (default)
- Assign your **Streamed Skeleton** and **Streamed Avatar**

### 3. Create a Configuration Asset

1. Right-click in Project window
2. Select `Create → Motion Tracking → Configuration`
3. Enable the modules you need:
   - Torso Module
   - Foot Module
   - Arms Module
   - Head Module
   - Balance Module

### 4. Assign Configuration

Drag your configuration asset to the **Config** field on `MotionTrackingManager` or `MultiplayerTrackingManager`.

### 5. Access Tracking Data -- Singleplayer

To directly find input actions from the input device:

There are two ways to access tracking data. You can access it directly using the input device, or by using the provided Input Action Asset. 

#### Calling Directly

```csharp
using UnityEngine;
using UnityEngine.InputSystem;

public class DirectInputTrackingExample : MonoBehaviour
{
    void Update()
    {
        // find captury input device
        var captury = InputSystem.GetDevice<CapturyInput>();
        
        if (captury != null)
        {
            // check if walking
            if (captury.isWalking.isPressed)
            {
                // read the value of our speed
                float speed = captury.walkSpeed.ReadValue();
                Debug.Log($"Walking at {speed} m/s");
            }
            
            // check weight shift
            if (captury.weightShiftLeft.isPressed)
            {
                Debug.Log("Weight shifted left");
            }
            
            // get foot position
            Vector3 leftFoot = captury.leftFootPosition.ReadValue();

            // do whatever you want with these numbers!
            // for now, print the x, y, and z separately
            Debug.Log($"Left foot X: {leftFoot.x}");
            Debug.Log($"Left foot Y: {leftFoot.y}");
            Debug.Log($"Left foot Z: {leftFoot.z}");
        }
    }
}
```
#### Using Input Action Asset

You can also use an `InputActionAsset`, or the MotionTracking one created for you already:

```csharp
using UnityEngine;
using UnityEngine.InputSystem;

public class InputActionAssetTrackingExample : MonoBehaviour
{
    // assign Input Action Asset in the Inspector
    public InputActionAsset inputActions;

    private InputAction isWalkingAction;
    private InputAction walkSpeedAction;
    private InputAction weightShiftLeftAction;

    void Awake()
    {
        // must be in AWAKE!
        // assuming you're using the given MotionTracking asset, action maps are separated by module
        var footMap = inputActions.FindActionMap("Foot");
        var torsoMap = inputActions.FindActionMap("Torso");

        // find specific actions
        isWalkingAction = footMap.FindAction("IsWalking");
        walkSpeedAction = footMap.FindAction("WalkSpeed");
        weightShiftLeftAction = torsoMap.FindAction("WeightShiftLeft");
    }

    void OnEnable()
    {
        // enable the actions
        isWalkingAction.Enable();
        walkSpeedAction.Enable();
        weightShiftLeftAction.Enable();

        isWalkingAction.performed += OnWalk;
        weightShiftLeftAction.performed += OnWeightShiftLeft;
    }

    void OnDisable()
    {
        // disable the actions
        isWalkingAction.Disable();
        walkSpeedAction.Disable();
        weightShiftLeftAction.Disable();

        isWalkingAction.performed -= OnWalk;
        weightShiftLeftAction.performed -= OnWeightShiftLeft;
    }

    void OnWalk(InputAction.CallbackContext ctx)
    {
        float walkSpeed = walkSpeedAction.ReadValue<float>();
        Debug.Log($"Walking at {walkSpeed} m/s");
    }

    void OnWeightShift(InputAction.CallbackContext ctx)
    {
        Debug.Log("Weight shifted left");
    }
}
```

Though the setup for this is a little longer, it is a much better, more modular way to call the actions, especially if you have more complex control schemes. 

### 6. Access Tracking Data -- Multiplayer

For multiplayer scenarios, the recommended approach is **direct device access**. Each player script finds and stores a reference to its specific CapturyInput device based on the device's usage tag (e.g., `Player1`, `Player2`).

This approach is simpler and more reliable than binding overrides, as each player maintains a direct reference to their own device and reads from it directly.
```csharp
using UnityEngine;
using UnityEngine.InputSystem;

public class MultiplayerTrackingExample : MonoBehaviour
{
    public int playerNumber;
    private CapturyInput myDevice;
    
    void Start()
    {
        FindMyDevice();
    }
    
    private void FindMyDevice()
    {
        foreach (var device in InputSystem.devices)
        {
            if (device is CapturyInput capturyDevice)
            {
                // check if this device has the usage we're looking for
                bool isMyDevice = false;
                foreach (var usage in device.usages)
                {
                    if (usage == $"Player{playerNumber}")
                    {
                        isMyDevice = true;
                        break;
                    }
                }
                
                if (isMyDevice)
                {
                    myDevice = capturyDevice;
                    Debug.Log($"Player {playerNumber}: Found my device - {myDevice.name}");
                    return;
                }
            }
        }
        
        Debug.LogWarning($"Player {playerNumber}: Could not find device with usage 'Player{playerNumber}'");
    }
    
    void Update()
    {
        // if device not found yet, retry periodically
        if (myDevice == null)
        {
            if (Time.frameCount % 60 == 0)
            {
                FindMyDevice();
            }
            return;
        }
        
        // read values directly from the device
        bool isWalking = myDevice.isWalking.isPressed;
        float walkSpeed = myDevice.walkSpeed.ReadValue();
        
        // check for button state changes
        if (myDevice.weightShiftLeft.wasPressedThisFrame)
        {
            Debug.Log($"Player {playerNumber}: Weight shifted left");
        }
        
        if (myDevice.weightShiftRight.wasPressedThisFrame)
        {
            Debug.Log($"Player {playerNumber}: Weight shifted right");
        }
        
        // check continuous values
        if (isWalking && Time.frameCount % 60 == 0)
        {
            Debug.Log($"Player {playerNumber}: Walking at {walkSpeed} m/s");
        }
    }
}
```

**How it works:**
1. Each player script searches for a CapturyInput device with the matching `Player{N}` usage
2. Once found, the script stores a direct reference to that specific device
3. All tracking data is read directly from the device using `.ReadValue()` or `.isPressed`
4. Button-like controls (e.g., `weightShiftLeft`) support state checks like `wasPressedThisFrame`
5. If the device isn't found immediately (e.g., skeleton spawns late), the script retries every 60 frames

**Alternative Approach - InputAction Binding Overrides:**

If you prefer to use InputActions, you can instance your InputActionAsset and apply binding overrides. However, this approach can be more complex:
```csharp
// in Awake():
instancedActions = Instantiate(inputActions);
var footMap = instancedActions.FindActionMap("Foot");
isWalkingAction = footMap.FindAction("IsWalking");

// override binding to specific player's device
// format: <DeviceType>/{Usage}/controlPath
isWalkingAction.ApplyBindingOverride($"<CapturyInput>/{{Player{playerNumber}}}/isWalking");
```

For most use cases, **direct device access is recommended** as it's more straightforward and avoids potential binding resolution issues in multiplayer scenarios.

---

## Configuration Options

### Motion Tracking Manager
- **Modular System** - Use what you need, don't use what you don't
- **Configuration Scriptables** - Create your own configurations and swap between them in editor or during runtime
- **Calibration Setup** - Automatically or manually calibrate your modules, setting your own delays and calling the calibrate method when necessary

### Multiplayer Manager
- **Maximum Players** - Set a number of max players accepted
- **Calibration Preferences** - Decide if calibration will happen automatically, and the delay (in seconds) between calibrating skeletons

### Torso Module
- **Weight Shift Threshold** - Distance required to trigger weight shift detection
- **Neutral Zone Width** - Size of neutral zone to prevent flutter
- **Bent Over Angle** - Forward bend angle to trigger detection
- **Whole Body Movement Threshold** - Ratio to ignore coordinated movement
- **Squat Suppresses Shift At** - Squat depth above which weight-shift detection is suppressed, so bending into a squat isn't misread as a lateral shift
- **Squat Detection** *(opt-in, `isSquatTracked`)* - Normalized squat depth threshold, walking-speed suppression (filters knee flexion from gait), and minimum landmark confidence (MediaPipe) before trusting the knee-angle signal; requires left/right knee joint names in addition to pelvis/spine

### Foot Module
- **Foot Raise Threshold** - Height difference to trigger foot raise
- **Hip Abduction Distance** - Additional spread distance for abduction
- **Min Lift Height** - Minimum height for valid movements
- **Position Tracking** - Relative or absolute positioning

### Walk Tracking
- **Walk Speed Threshold** - Minimum speed to start walk detection
- **Minimum Walk Duration** - Time required to confirm walking
- **Walk Stop Threshold** - Speed below which walking stops

### Gait Analysis
- **Minimum Cycles** - Required complete cycles for reliable analysis
- **Step Time Range** - Min/max reasonable step times (filters outliers)
- **Consistency Calculation** - Based on step time variance

### Arms/Hands Module
- **Hand Raise Threshold** - Height above shoulder to trigger
- **Min Height Gain** - Minimum lift from neutral position

### Head Module
- **Nod/Shake Thresholds** - Rotation angles for gesture detection
- **Gesture Speed** - Time window for gesture completion
- **Gesture Timeout** - Maximum active duration

### Balance Module
- **Sway and Stability Thresholds** - Max stability in m/s
- **Center of Mass Frame History** - Frames of CoM history to keep

---

## Joint Name Configuration

Configure joint names in your configuration asset to match your skeleton. You can upload your own skeleton, but by default it assumes the names of the model that comes with the Captury plugin. Note that **each joint can only be accessed by one module at a time**.

| Module | Joint | Default Name |
|--------|-------|--------------|
| Torso | Pelvis | `Hips` |
| Balance | Bottom of Spine | `Spine1` |
| Torso | Top of Spine | `Spine4` |
| Head | Head | `Head` |
| Arms | Left Shoulder | `LeftShoulder` |
| Arms | Right Shoulder | `RightShoulder` |
| Balance | Left Forearm | `LeftForearm` |
| Balance | Right Forearm | `RightForearm` |
| Arms | Left Hand | `LeftHand` |
| Arms | Right Hand | `RightHand` |
| Balance | Left Leg | `LeftLeg` |
| Balance | Right Leg | `RightLeg` |
| Feet | Left Foot | `LeftFoot` |
| Feet | Right Foot | `RightFoot` |
| Balance | Bottom of Spine | `Spine1` |
| Balance | Left Forearm | `LeftForeArm` |
| Balance | Right Forearm | `RightForeArm` |
| Balance | Left Leg | `LeftLeg` |
| Balance | Right Leg | `RightLeg` |
| Balance | Left Toe Base | `LeftToeBase` |
| Balance | Right Toe Base | `RightToeBase` |
| Torso | Left Knee *(squat detection only)* | `LeftLeg` |
| Torso | Right Knee *(squat detection only)* | `RightLeg` |

Defaults above are for Captury. Kinect and MediaPipe use their own default joint names (e.g. Kinect's pelvis is `SpineBase`) — `ApplyJointDefaults` fills these in automatically when you set a configuration's motion source, and `MotionTrackingOrchestrator` keeps the config's source in sync with whichever manager is active.

---

## Multi-Source Tracking

The toolkit ships three interchangeable `IMotionTrackingManager` implementations — Captury (`MotionTrackingManager`), Kinect (`KinectMotionTrackingManager`), and MediaPipe (`MediaPipeMotionTrackingManager`) — plus `MotionTrackingOrchestrator`, a facade that activates exactly one of them and exposes a single, source-agnostic API to the rest of your game. All tracking modules, the Input System actions, room calibration, and `MotionRecorder` work unchanged regardless of which source is active.

### Setup

1. Add a `MotionTrackingOrchestrator` to your scene (or use the prefab layout below) with a child manager per source you want to support:
   ```
   MotionTrackingOrchestrator
     ├── MotionTrackingManager           (Captury)
     ├── KinectMotionTrackingManager     (Kinect)
     └── MediaPipeMotionTrackingManager  (MediaPipe)
   ```
2. Set **Active Source** in the inspector, and assign the same `MotionTrackingConfiguration` you'd use for a single-source setup — the orchestrator keeps `config.motionSource` in sync with the active manager and applies joint defaults automatically.
3. Only the active manager's GameObject is enabled; the others are deactivated but stay in the hierarchy so you can switch later.

### Switching sources

- **In the Editor / at dev time**: `Tools → Motion Tracking → Source Override` sets a `PlayerPrefs`-backed override that every `MotionTrackingOrchestrator` picks up on `Awake` (takes effect on next Play, not a hot-swap of a running scene).
- **In code**: `MotionTrackingOrchestrator.SetGlobalSource(MotionSource.Kinect)` / `ClearGlobalSource()`, e.g. from a bootstrap scene, for a persistent override.
- **Per-instance**: the inspector's **Active Source** field, used if no override is set.
- Override priority: code (`GlobalSourceOverride`) → `PlayerPrefs` → inspector value.
- Scene-to-scene handoff: if a `DontDestroyOnLoad` orchestrator already exists when a new scene loads, the incoming one hands off its config to the survivor and destroys itself, so the active source carries forward while each scene's module config is respected.

### Source-specific prerequisites

| Source | Additional dependency |
|--------|------------------------|
| Captury | Captury Unity Plugin (included), `CapturyNetworkPlugin` connected to CapturyLive |
| Kinect | `Windows.Kinect` plugin and a `BodySourceManager` (from the Kinect view/plugin), Windows + Kinect sensor |
| MediaPipe | `mediapipe_sender.py` running alongside Unity, streaming pose landmarks over UDP to a `MediaPipeInput` component |

---

## Room Calibration

Sources that support room-scale tracking (currently MediaPipe and Kinect) implement `IBoundaryWalkable`, letting you map a physical room onto game space and optionally capture a walkable boundary:

- **`StartRoomCalibration()`** - captures origin position, facing (yaw), and floor height from the player's current pose; drives the `originOffset` / `yawDegrees` / `floorHeight` / `scale` transform used by `RoomCalibration.GetRoomToGame()`
- **`SetBoundaryPoints(Vector3[])`** / boundary walking - records a polygon (in room-frame XZ) that consumers can use to keep players inside safe tracking bounds
- **`SaveRoomCalibration(name)`** / **`MergeSavedBoundary(name)`** - persists a named calibration (via `RoomCalibrationStore`) and can merge a previously saved boundary into a fresh calibration
- **`TryGetRoomPosition(out Vector3)`** - the live, calibrated game-space position, exposed on `IMotionTrackingManager` (and forwarded through `MotionTrackingOrchestrator`) so consumers don't need to know which source is active
- **`HasRoomBounds`** / **`GetRoomBoundary()`** / **`RoomMinTrackingDistance`** - query the active boundary and minimum reliable tracking distance

Set `defaultRoomCalibrationName` on a manager to auto-load a saved calibration on startup instead of running a live one. Calibrations record the source they were captured with (and a device fingerprint, where available) so a mismatched load can warn instead of silently applying the wrong transform.

---

## Motion Recording

`MotionRecorder` (`Runtime/Scripts/Recording/MotionRecorder.cs`) records tracking sessions to two files per recording:

- **JSON** - full-fidelity record: per-frame joint positions/rotations plus the complete `CapturyInputState` (weight shift, squat depth, gait metrics, balance/sway, etc.) if `recordInputStates` is enabled
- **`.c3d`** - positions-only biomechanics file, written natively via the vendored c3d4sharp writer (no external Python dependency required). Coordinates are millimeters, recentered on the pelvis (`SpineBase`) per frame, with a residual of `-1` for any joint missing that frame

`MotionRecorder` is source-agnostic: it resolves the active `IMotionTrackingManager` at runtime (preferring `MotionTrackingOrchestrator.Instance` when present) and reads joints through `JointNameSets`, so the same component records Captury, Kinect, or MediaPipe sessions without changes.

### Usage

1. Add `MotionRecorder` to a GameObject in your scene (alongside or near your tracking manager).
2. Optionally call `SetSessionTag("participantID_sessionNumber")` before recording to name output files.
3. Call `StartRecording()` / `StopRecording()` — files are written to `Application.persistentDataPath/MotionRecordings/` by default (configurable via `outputFolderName`).
4. Check `LastConversionStatus` / `LastC3DPath` after `StopRecording()` to confirm the `.c3d` write succeeded.

---

## Requirements

- Unity 2020.3 or later
- Unity Input System 1.4.0 or later
- Captury Unity Plugin (included)
- Windows.Kinect plugin (only if using the Kinect source)
- `mediapipe_sender.py` + a `MediaPipeInput` UDP receiver (only if using the MediaPipe source)

---

### Third-Party Licenses

This package includes the **Captury Unity Plugin**:
- Copyright © 2017 thecaptury
- Licensed under MIT License
- See `Runtime/ThirdParty/Captury/LICENSE.txt`

---

## Version History

### 1.0.0
- Initial release
- Torso, foot, arm, and head tracking modules
- Walk detection and gait analysis
- Input System integration

### 1.1.0 & 1.1.1
- Added balance tracking support
- Fixed foot tracking relative position bug

### 1.2.0
- Added Multiplayer support (multiple skeletons tracked by the same system)

### 2.0.0
- Renamed the package from Captury Unity Toolkit to **Motion Tracking Toolkit**, reflecting multi-source support
- Added Kinect and MediaPipe motion sources alongside Captury, unified behind `MotionTrackingOrchestrator` and the `MotionSource` enum
- Added room calibration and boundary walking (`IBoundaryWalkable`, `RoomCalibrationStore`) for room-scale sources
- Added squat detection to the Torso module, with walking-gait suppression and MediaPipe landmark-confidence gating
- Added `MotionRecorder`: full-fidelity JSON session recording plus native `.c3d` export via a vendored c3d4sharp writer
