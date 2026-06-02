using System.IO;
using UnityEngine;

public static class CalibrationStore
{
    private const string SubFolder = "Calibrations";

    private static string FolderPath =>
        Path.Combine(Application.persistentDataPath, SubFolder);

    private static string PathFor(string calibrationName) =>
        Path.Combine(FolderPath, calibrationName + ".json");

    public static bool Exists(string calibrationName) =>
        !string.IsNullOrEmpty(calibrationName) && File.Exists(PathFor(calibrationName));

    public static void Save(CalibrationBundle bundle)
    {
        if (bundle == null || string.IsNullOrEmpty(bundle.calibrationName)) return;
        Directory.CreateDirectory(FolderPath);
        File.WriteAllText(PathFor(bundle.calibrationName), JsonUtility.ToJson(bundle, true));
        Debug.Log($"CalibrationStore: Saved '{bundle.calibrationName}' to {PathFor(bundle.calibrationName)}");
    }

    public static CalibrationBundle Load(string calibrationName)
    {
        if (!Exists(calibrationName)) return null;
        string text = File.ReadAllText(PathFor(calibrationName));
        return JsonUtility.FromJson<CalibrationBundle>(text);
    }
}
