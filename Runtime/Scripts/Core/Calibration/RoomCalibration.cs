using System;
using UnityEngine;

[Serializable]
public class RoomCalibration
{
    public string calibrationName;
    public string source;       // MotionSource.ToString(), for mismatch warnings on load
    public string sourceFingerprint;   // device serial (or similar); empty/unknown = unverifiable, always trusted
    public float timestamp;

    public Vector3 originOffset;   // reference-frame hip position at room-center capture
    public float yawDegrees;       // player facing at capture; aligns room-forward with game-forward
    // FLOOR MODEL: single height — min ankle Y at capture time.
    // Future: replace with a plane (normal + point) for sloped/uneven floors.
    public float floorHeight;
    public float scale = 1.0f;     // room→game uniform scale; 1.0 = 1:1 (metres)

    // Optional boundary walk — XZ positions in room frame. Empty = not captured.
    public Vector2[] boundaryPoints = new Vector2[0];
    // Optional min reliable tracking distance from originOffset (room-frame metres). 0 = not captured.
    public float minTrackingDistance = 0f;

    public bool HasBoundary => boundaryPoints != null && boundaryPoints.Length >= 3;
    public bool HasMinTrackingDistance => minTrackingDistance > 0f;

    /// <summary>
    /// Returns boundary polygon in game space (XZ plane, Y=0). Empty array if HasBoundary==false.
    /// </summary>
    public Vector3[] GetGameSpaceBoundary()
    {
        if (!HasBoundary) return System.Array.Empty<Vector3>();
        var m = GetRoomToGame();
        var result = new Vector3[boundaryPoints.Length];
        for (int i = 0; i < boundaryPoints.Length; i++)
        {
            // use floorHeight as Y so the transform maps it to Y=0 in game space
            var roomPoint = new Vector3(boundaryPoints[i].x, floorHeight, boundaryPoints[i].y);
            var gp = m.MultiplyPoint3x4(roomPoint);
            result[i] = new Vector3(gp.x, 0f, gp.z);
        }
        return result;
    }

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
