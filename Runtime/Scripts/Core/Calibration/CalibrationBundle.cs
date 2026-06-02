using System;
using System.Collections.Generic;

[Serializable]
public class CalibrationBundleEntry
{
    public string moduleType;   // module.GetType().Name — used to match on load
    public string json;         // the module's own serialized snapshot string
}

[Serializable]
public class CalibrationBundle
{
    public string calibrationName;
    public string source;       // MotionSource.ToString(), for mismatch warnings on load
    public float timestamp;
    public List<CalibrationBundleEntry> entries = new List<CalibrationBundleEntry>();
}
