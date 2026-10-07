using UnityEngine;

// base for per-module configuration, each tracking module extends this
// instances are stored in MotionTrackingConfiguration's module list
//
// Every distance in a module config is in metres and every angle in degrees, measured on the
// skeleton the active manager produces. At runtime, modules read thresholds through
// MotionTrackingTuning, so the global sensitivity knob can retune every config at once without
// editing the assets (see MotionTrackingTuning for which values scale and which don't).

// to create a new module:
//   1. extend ModuleConfiguration with your settings
//   2. extend MotionTrackingModule with your tracking logic
//   3. override ApplyJointDefaults() with mappings for each MotionSource
//   4. add your config to the MotionTrackingConfiguration list in the inspector
[System.Serializable]
public abstract class ModuleConfiguration
{
    [Header("Base Module Settings")]
    [Tooltip("Turn this module on. Disabled modules aren't created, and their joints aren't required.")]
    public bool enabled = false;

    [Range(0.1f, 3.0f)]
    [Tooltip("Gain on this module's continuous outputs (positions, sway, weight-shift amount). Also multiplied by the global MotionTrackingTuning sensitivity. Does not change boolean detection thresholds.")]
    public float sensitivity = 1.0f;

    [Tooltip("Log this module's detections and calibration values to the Console.")]
    public bool debugMode = false;

    // returns the joint names that this module tracks
    public abstract string[] GetRequiredJointNames();

    // creates an instance of the module based on this configuration
    public abstract MotionTrackingModule CreateModule(GameObject moduleParent);

    // sets joint name fields to the correct defaults for the given motion source.
    // override in each module config to map joint names for Captury, Kinect, etc.
    // called by the editor when the source changes, and by managers on init.
    public virtual void ApplyJointDefaults(MotionSource source) { }
}