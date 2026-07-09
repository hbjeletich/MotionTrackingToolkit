using System.IO;
using UnityEngine;

public static class RoomCalibrationStore
{
    private const string SubFolder = "RoomCalibrations";

    private static string FolderPath =>
        Path.Combine(Application.persistentDataPath, SubFolder);

    private static string PathFor(string calibrationName) =>
        Path.Combine(FolderPath, calibrationName + ".json");

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
}
