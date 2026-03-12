using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[CreateAssetMenu(fileName = "MotionConfig", menuName = "Motion Tracking/Configuration")]
public class MotionTrackingConfiguration : ScriptableObject
{
    [Header("Configuration Info")]
    public string configurationName = "Default";

    [TextArea(2, 4)]
    public string description = "Default motion tracking configuration.";

    [Header("Motion Source")]
    [Tooltip("Which motion capture system provides skeleton data. Non-Custom sources auto-configure joint names.")]
    public MotionSource motionSource = MotionSource.Custom;

    [Header("System Settings")]
    public float calibrationDelay = 2.0f;

    [Tooltip("Number of frames to average during calibration")]
    public int calibrationFrames = 30;

    [SerializeReference]
    public List<ModuleConfiguration> modules = new List<ModuleConfiguration>();

    // true when joint names are fixed by the source (not user-editable)
    public bool HasFixedJointNames => motionSource != MotionSource.Custom;

    // apply joint name defaults from the current motion source to all modules.
    public void ApplySourceDefaults()
    {
        if (motionSource == MotionSource.Custom) return;

        foreach (var module in modules)
        {
            module?.ApplyJointDefaults(motionSource);
        }
    }

    public T GetModuleConfig<T>() where T : ModuleConfiguration
    {
        for (int i = 0; i < modules.Count; i++)
        {
            if (modules[i] is T typed)
                return typed;
        }
        return null;
    }

    public IEnumerable<ModuleConfiguration> GetEnabledModules()
    {
        return modules.Where(m => m != null && m.enabled);
    }

    public bool IsModuleEnabled<T>() where T : ModuleConfiguration
    {
        var config = GetModuleConfig<T>();
        return config != null && config.enabled;
    }
}