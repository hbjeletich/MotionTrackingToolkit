using UnityEngine;

// base for per-module configuration, each tracking module extends this
// instances are stored in MotionTrackingConfiguration's module list

// to create a new module:
//   1. extend ModuleConfiguration with your settings
//   2. extend MotionTrackingModule with your tracking logic
//   3. override ApplyJointDefaults() with mappings for each MotionSource
//   4. add your config to the MotionTrackingConfiguration list in the inspector
[System.Serializable]
public abstract class ModuleConfiguration
{
    [Header("Base Module Settings")]
    public bool enabled = false;

    [Range(0.1f, 3.0f)]
    public float sensitivity = 1.0f;

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