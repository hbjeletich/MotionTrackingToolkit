using System.Collections.Generic;
using Windows.Kinect;

namespace CapturyToolkit.Kinect
{

// maps Kinect JointType values to the string names that tracking modules use.
// this is the single source of truth for joint naming in the Kinect pipeline.
//
// usage:
//   string name = KinectJointMapper.GetJointName(JointType.HandLeft);   // → "HandLeft"
//   JointType jt = KinectJointMapper.GetJointType("HandLeft");          // → JointType.HandLeft
//   JointType[] all = KinectJointMapper.AllJointTypes;                  // all 25 joints

public static class KinectJointMapper
{
    private static readonly Dictionary<JointType, string> jointTypeToName = new Dictionary<JointType, string>
    {
        // spine chain
        { JointType.SpineBase,      "SpineBase" },
        { JointType.SpineMid,       "SpineMid" },
        { JointType.SpineShoulder,  "SpineShoulder" },
        { JointType.Neck,           "Neck" },
        { JointType.Head,           "Head" },

        // left arm
        { JointType.ShoulderLeft,   "ShoulderLeft" },
        { JointType.ElbowLeft,      "ElbowLeft" },
        { JointType.WristLeft,      "WristLeft" },
        { JointType.HandLeft,       "HandLeft" },
        { JointType.HandTipLeft,    "HandTipLeft" },
        { JointType.ThumbLeft,      "ThumbLeft" },

        // right arm
        { JointType.ShoulderRight,  "ShoulderRight" },
        { JointType.ElbowRight,     "ElbowRight" },
        { JointType.WristRight,     "WristRight" },
        { JointType.HandRight,      "HandRight" },
        { JointType.HandTipRight,   "HandTipRight" },
        { JointType.ThumbRight,     "ThumbRight" },

        // left leg
        { JointType.HipLeft,        "HipLeft" },
        { JointType.KneeLeft,       "KneeLeft" },
        { JointType.AnkleLeft,      "AnkleLeft" },
        { JointType.FootLeft,       "FootLeft" },

        // right leg
        { JointType.HipRight,       "HipRight" },
        { JointType.KneeRight,      "KneeRight" },
        { JointType.AnkleRight,     "AnkleRight" },
        { JointType.FootRight,      "FootRight" },
    };

    private static readonly Dictionary<string, JointType> nameToJointType;

    public static readonly JointType[] AllJointTypes;

    static KinectJointMapper()
    {
        nameToJointType = new Dictionary<string, JointType>();
        foreach (var kvp in jointTypeToName)
        {
            nameToJointType[kvp.Value] = kvp.Key;
        }

        AllJointTypes = new JointType[jointTypeToName.Count];
        int i = 0;
        foreach (var key in jointTypeToName.Keys)
        {
            AllJointTypes[i++] = key;
        }
    }

    public static string GetJointName(JointType type)
    {
        jointTypeToName.TryGetValue(type, out string name);
        return name;
    }

    public static JointType GetJointType(string name)
    {
        if (nameToJointType.TryGetValue(name, out JointType type))
            return type;
        return (JointType)(-1);
    }

    public static bool HasJoint(string name)
    {
        return nameToJointType.ContainsKey(name);
    }

    public static int JointCount => jointTypeToName.Count;
}

} // namespace CapturyToolkit.Kinect