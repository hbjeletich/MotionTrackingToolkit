using System.Collections.Generic;
using UnityEngine;

// abstract base for all motion tracking modules. handles:
//   - Joint resolution (modules just declare names, base resolves them)
//   - Calibration management (current + previous snapshots, swap/restore)
//   - Common enable/sensitivity/debug properties from config
//
// to implement a new module, override:
//   - GetModuleConfig()         → return your typed config from the manager
//   - CaptureCalibration()      → snapshot your neutral positions into a CalibrationSnapshot
//   - OnCalibrationApplied()    → (optional) react when calibration is loaded/swapped
//   - UpdateTracking(ref state) → your per-frame tracking logic
public abstract class MotionTrackingModule : MonoBehaviour
{
    #region Fields

    protected IMotionTrackingManager manager;

    // joints by name during calibration, access via GetJoint()
    protected Dictionary<string, Transform> resolvedJoints = new Dictionary<string, Transform>();

    private CalibrationSnapshot _currentCalibration;
    private CalibrationSnapshot _previousCalibration;

    #endregion

    #region Public Properties

    public bool IsCalibrated => _currentCalibration != null;
    public CalibrationSnapshot CurrentCalibration => _currentCalibration;
    public CalibrationSnapshot PreviousCalibration => _previousCalibration;
    public bool HasPreviousCalibration => _previousCalibration != null;

    // pull from the module's own config
    public bool IsEnabled => GetModuleConfig()?.enabled ?? false;
    public float Sensitivity => GetModuleConfig()?.sensitivity ?? 1.0f;
    public bool DebugMode => GetModuleConfig()?.debugMode ?? false;

    #endregion

    #region Abstract Methods — Subclasses Must Implement

    // return this module's typed configuration from the manager's config.
    public abstract ModuleConfiguration GetModuleConfig();

    // Capture a calibration snapshot from the currently resolved joints.
    protected abstract CalibrationSnapshot CaptureCalibration();

    // per-frame tracking update, modify the input state as needed. called only if IsEnabled.
    public abstract void UpdateTracking(ref CapturyInputState state);

    #endregion

    #region Virtual Methods — Optional Overrides

    // Called after calibration data is applied (both on fresh and on restore)
    protected virtual void OnCalibrationApplied() { }

    // called once during module setup, override to initialize data structures.
    // always call base.Initialize(manager)
    public virtual void Initialize(IMotionTrackingManager manager)
    {
        this.manager = manager;
        Debug.Log($"{GetType().Name}: Initialized");
    }

    #endregion

    #region Joint Resolution

    // get the joint names this module requires from config
    public string[] GetRequiredJointNames()
    {
        return GetModuleConfig()?.GetRequiredJointNames() ?? System.Array.Empty<string>();
    }

    // check if all required joints can be found via the manager
    public bool HasRequiredJoints()
    {
        string[] names = GetRequiredJointNames();
        for (int i = 0; i < names.Length; i++)
        {
            if (manager?.GetJointByName(names[i]) == null)
            {
                if (DebugMode)
                    Debug.LogWarning($"{GetType().Name}: Missing required joint '{names[i]}'");
                return false;
            }
        }
        return true;
    }

    // resolve all required joints into the resolvedJoints dictionary, returns false if any are missing
    protected bool ResolveJoints()
    {
        resolvedJoints.Clear();
        string[] names = GetRequiredJointNames();

        for (int i = 0; i < names.Length; i++)
        {
            Transform joint = manager?.GetJointByName(names[i]);
            if (joint == null)
            {
                Debug.LogError($"{GetType().Name}: Could not resolve joint '{names[i]}'");
                return false;
            }
            resolvedJoints[names[i]] = joint;

            if (DebugMode)
                Debug.Log($"{GetType().Name}: Resolved joint '{names[i]}' at {joint.position}");
        }

        return true;
    }


    // get a previously resolved joint by name.
    protected Transform GetJoint(string jointName)
    {
        resolvedJoints.TryGetValue(jointName, out Transform t);
        return t;
    }

    #endregion

    #region Calibration Management

    // run calibration: resolves joints, captures a snapshot, stores it as current
    public void Calibrate()
    {
        if (!ResolveJoints())
        {
            Debug.LogError($"{GetType().Name}: Calibration failed — could not resolve all required joints");
            return;
        }

        CalibrationSnapshot snapshot = CaptureCalibration();
        if (snapshot == null)
        {
            Debug.LogError($"{GetType().Name}: CaptureCalibration() returned null — calibration aborted");
            return;
        }

        snapshot.timestamp = Time.time;

        // push current → previous, set new → current
        _previousCalibration = _currentCalibration?.Clone();
        _currentCalibration = snapshot;

        OnCalibrationApplied();

        Debug.Log($"{GetType().Name}: Calibrated successfully at t={snapshot.timestamp:F2}");
    }

    // swap back to the previous calibration. returns false if no previous exists.
    public bool LoadPreviousCalibration()
    {
        if (_previousCalibration == null)
        {
            Debug.LogWarning($"{GetType().Name}: No previous calibration available");
            return false;
        }

        // swap current ↔ previous
        CalibrationSnapshot temp = _currentCalibration;
        _currentCalibration = _previousCalibration;
        _previousCalibration = temp;

        OnCalibrationApplied();

        Debug.Log($"{GetType().Name}: Restored previous calibration (from t={_currentCalibration.timestamp:F2})");
        return true;
    }

    // explicitly set calibration data
    public void SetCalibration(CalibrationSnapshot snapshot)
    {
        if (snapshot == null) return;
        _previousCalibration = _currentCalibration?.Clone();
        _currentCalibration = snapshot;
        OnCalibrationApplied();
    }

    // clear all calibration data.
    public void ClearCalibration()
    {
        _previousCalibration = null;
        _currentCalibration = null;
    }

    // serialize the current calibration to a JSON string, or null if not calibrated
    public abstract string SerializeCalibration();

    // rebuild and apply a calibration from a JSON string produced by SerializeCalibration
    public abstract void DeserializeCalibration(string json);

    #endregion

    #region Utility

    protected Vector3 NormalizeEulerAngles(Vector3 angles)
    {
        angles.x = NormalizeAngle(angles.x);
        angles.y = NormalizeAngle(angles.y);
        angles.z = NormalizeAngle(angles.z);
        return angles;
    }

    protected float NormalizeAngle(float angle)
    {
        while (angle > 180f) angle -= 360f;
        while (angle < -180f) angle += 360f;
        return angle;
    }

    #endregion
}