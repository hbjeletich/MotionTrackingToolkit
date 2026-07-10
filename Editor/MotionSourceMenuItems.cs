#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

// Tools > Motion Tracking > Source Override
// Sets a PlayerPrefs key that every MotionTrackingOrchestrator reads on Awake,
// overriding its inspector activeSource for the duration of a dev session.
// Takes effect on the next Play — does not hot-swap a running scene.
public static class MotionSourceMenuItems
{
    private const string PrefKey = "MTM.SourceOverride";
    private const string MenuBase = "Tools/Motion Tracking/Source Override/";

    [MenuItem(MenuBase + "Kinect")]
    static void SetKinect()     => SetSource(MotionSource.Kinect);

    [MenuItem(MenuBase + "Captury")]
    static void SetCaptury()    => SetSource(MotionSource.Captury);

    [MenuItem(MenuBase + "MediaPipe")]
    static void SetMediaPipe()  => SetSource(MotionSource.MediaPipe);

    [MenuItem(MenuBase + "OAK-D")]
    static void SetOakD()       => SetSource(MotionSource.OakD);

    [MenuItem(MenuBase + "Clear (use inspector values)")]
    static void ClearOverride()
    {
        PlayerPrefs.DeleteKey(PrefKey);
        PlayerPrefs.Save();
        Debug.Log("[MotionTracking] Source override cleared — Orchestrators will use their inspector values on next Play.");
    }

    static void SetSource(MotionSource source)
    {
        PlayerPrefs.SetInt(PrefKey, (int)source);
        PlayerPrefs.Save();
        Debug.Log($"[MotionTracking] Source override → {source}. Takes effect on next Play.");
    }

    // Validation methods drive the checkmarks next to each menu item.
    [MenuItem(MenuBase + "Kinect",    true)] static bool VKinect()    => Validate(MotionSource.Kinect);
    [MenuItem(MenuBase + "Captury",   true)] static bool VCaptury()   => Validate(MotionSource.Captury);
    [MenuItem(MenuBase + "MediaPipe", true)] static bool VMediaPipe() => Validate(MotionSource.MediaPipe);
    [MenuItem(MenuBase + "OAK-D",     true)] static bool VOakD()      => Validate(MotionSource.OakD);

    static bool Validate(MotionSource source)
    {
        int stored = PlayerPrefs.GetInt(PrefKey, -1);
        Menu.SetChecked(MenuBase + source, stored == (int)source);
        return true;
    }
}
#endif
