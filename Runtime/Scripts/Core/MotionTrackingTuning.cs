using UnityEngine;

// Global, session-wide difficulty knob for motion tracking.
//
// Every module reads its tuning numbers through the two helpers below instead of using the
// raw config value, so one multiplier retunes the whole toolkit without touching any of the
// per-game MotionTrackingConfiguration assets.
//
// There are two kinds of numbers, and they move in OPPOSITE directions:
//   - gains       (sensitivity)  → multiplied.  Higher global = bigger reported movement.
//   - thresholds  (*Threshold)   → divided.     Higher global = less movement needed to trigger.
//
// Scaling only the gain is not enough: every boolean gesture in the toolkit (footRaised,
// isSquatting, handRaised, isBentOver, walk start/stop, head direction) is a raw measurement
// compared against a threshold that the gain never touches. Without ScaleThreshold the knob
// would do nothing for the gesture-driven games, which is most of them.
//
// This is deliberately a plain static, not a MonoBehaviour or a ScriptableObject: it has to
// survive scene loads, be readable from a property getter on a hot path, and be settable from
// a debug menu that can appear in any scene.
public static class MotionTrackingTuning
{
    // 1 is "whatever the config asset says". The floor is well above 0 so ScaleThreshold can
    // never divide by zero and so the knob can't make a game completely uncontrollable.
    public const float MinGlobalSensitivity = 0.25f;
    public const float MaxGlobalSensitivity = 3.0f;
    public const float DefaultGlobalSensitivity = 1.0f;

    private const string PrefKey = "MotionTracking.GlobalSensitivity";

    private static float _globalSensitivity = DefaultGlobalSensitivity;
    private static bool _loaded;

    /// <summary>
    /// Whole-toolkit sensitivity multiplier. Persisted in PlayerPrefs so a playtest station
    /// keeps its setting across runs — which also means a stale value can quietly follow you
    /// into a data-collection session, so MotionRecorder stamps it into every recording's
    /// metadata and the F9 panel always shows the current value.
    /// </summary>
    public static float GlobalSensitivity
    {
        get
        {
            if (!_loaded) Load();
            return _globalSensitivity;
        }
        set
        {
            _loaded = true;
            _globalSensitivity = Mathf.Clamp(value, MinGlobalSensitivity, MaxGlobalSensitivity);
            // SetFloat is an in-memory write, so this is safe to call every frame from a slider
            // drag. The disk flush is Persist(), called once when the value settles.
            PlayerPrefs.SetFloat(PrefKey, _globalSensitivity);
        }
    }

    /// <summary>Flush the value to disk. Unity also flushes on a clean quit; call this so a crash
    /// or a force-quit mid-playtest doesn't lose the setting.</summary>
    public static void Persist() => PlayerPrefs.Save();

    /// <summary>True when the knob is off its default, i.e. tracking is not behaving as configured.</summary>
    public static bool IsModified => !Mathf.Approximately(GlobalSensitivity, DefaultGlobalSensitivity);

    public static void ResetToDefault()
    {
        GlobalSensitivity = DefaultGlobalSensitivity;
        Persist();
    }

    /// <summary>Scale a gain (a value multiplied into a reported position/sway). Higher = more movement reported.</summary>
    public static float ScaleGain(float configured) => configured * GlobalSensitivity;

    /// <summary>
    /// Scale a detection threshold. Divided, so a higher global sensitivity means a smaller
    /// real-world movement clears the bar. Only apply this to thresholds compared against a
    /// RAW measurement — a threshold compared against an already-gain-scaled value must be
    /// left alone or the knob applies twice.
    /// </summary>
    public static float ScaleThreshold(float configured) => configured / GlobalSensitivity;

    private static void Load()
    {
        _loaded = true;
        _globalSensitivity = Mathf.Clamp(
            PlayerPrefs.GetFloat(PrefKey, DefaultGlobalSensitivity),
            MinGlobalSensitivity, MaxGlobalSensitivity);
    }
}
