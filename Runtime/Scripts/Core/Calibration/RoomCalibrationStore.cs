using System.IO;
using UnityEngine;

public static class RoomCalibrationStore
{
    private const string SubFolder       = "RoomCalibrations";
    private const string BundleSubFolder = "RoomCalibrationBundles";

    private static string FolderPath =>
        Path.Combine(Application.persistentDataPath, SubFolder);

    private static string BundleFolderPath =>
        Path.Combine(Application.persistentDataPath, BundleSubFolder);

    private static string PathFor(string calibrationName) =>
        Path.Combine(FolderPath, calibrationName + ".json");

    private static string BundlePathFor(string name) =>
        Path.Combine(BundleFolderPath, name + ".json");

    // ── Legacy API (unchanged) ────────────────────────────────────────────────

    public static bool Exists(string calibrationName) =>
        !string.IsNullOrEmpty(calibrationName) && File.Exists(PathFor(calibrationName));

    public static void Save(RoomCalibration calibration)
    {
        if (calibration == null || string.IsNullOrEmpty(calibration.calibrationName)) return;
        Directory.CreateDirectory(FolderPath);
        File.WriteAllText(PathFor(calibration.calibrationName), JsonUtility.ToJson(calibration, true));
        Debug.Log($"RoomCalibrationStore: Saved '{calibration.calibrationName}' to {PathFor(calibration.calibrationName)}");
    }

    public static RoomCalibration Load(string calibrationName)
    {
        if (!Exists(calibrationName)) return null;
        string text = File.ReadAllText(PathFor(calibrationName));
        return JsonUtility.FromJson<RoomCalibration>(text);
    }

    // ── Bundle API ────────────────────────────────────────────────────────────

    public static bool BundleExists(string name) =>
        !string.IsNullOrEmpty(name) && File.Exists(BundlePathFor(name));

    public static void SaveBundle(RoomCalibrationBundle bundle)
    {
        if (bundle == null || string.IsNullOrEmpty(bundle.name)) return;
        Directory.CreateDirectory(BundleFolderPath);
        File.WriteAllText(BundlePathFor(bundle.name), JsonUtility.ToJson(bundle, true));
        Debug.Log($"RoomCalibrationStore: Saved bundle '{bundle.name}' to {BundlePathFor(bundle.name)}");
    }

    public static RoomCalibrationBundle LoadBundle(string name)
    {
        if (!BundleExists(name)) return null;
        string text = File.ReadAllText(BundlePathFor(name));
        return JsonUtility.FromJson<RoomCalibrationBundle>(text);
    }

    public static void DeleteBundle(string name)
    {
        if (!BundleExists(name)) return;
        File.Delete(BundlePathFor(name));
        Debug.Log($"RoomCalibrationStore: Deleted bundle '{name}'");
    }
}
