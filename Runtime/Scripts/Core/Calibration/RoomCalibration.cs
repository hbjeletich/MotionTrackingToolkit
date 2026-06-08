using System;
using UnityEngine;

[Serializable]
public class RoomCalibration
{
    public string calibrationName;
    public string source;       // MotionSource.ToString(), for mismatch warnings on load
    public float timestamp;

    public Vector3 originOffset;   // reference-frame hip position at room-center capture
    public float yawDegrees;       // player facing at capture; aligns room-forward with game-forward
    // FLOOR MODEL: single height — min ankle Y at capture time.
    // Future: replace with a plane (normal + point) for sloped/uneven floors.
    public float floorHeight;
    public float scale = 1.0f;     // room→game uniform scale; 1.0 = 1:1 (metres)

    /// <summary>
    /// Returns the matrix that maps a reference-camera-frame position into game space:
    /// center at captured origin (XZ) and floor (Y), rotate to align facing, then scale.
    /// Usage: gamePos = GetRoomToGame().MultiplyPoint3x4(absoluteHipPos)
    /// </summary>
    public Matrix4x4 GetRoomToGame()
    {
        // translate so originOffset.XZ → (0,_,0) and floorHeight → Y=0
        Matrix4x4 center = Matrix4x4.Translate(new Vector3(-originOffset.x, -floorHeight, -originOffset.z));
        Matrix4x4 rotate = Matrix4x4.Rotate(Quaternion.Euler(0f, -yawDegrees, 0f));
        Matrix4x4 scaleM = Matrix4x4.Scale(Vector3.one * scale);
        // applied right-to-left: center → rotate → scale
        return scaleM * rotate * center;
    }
}
