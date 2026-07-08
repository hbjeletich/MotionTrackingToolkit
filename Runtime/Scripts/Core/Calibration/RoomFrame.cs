using System;
using UnityEngine;

/// <summary>
/// Room coordinate frame: the origin/orientation/scale transform captured when the player
/// stands at the center of their play space. Separate from boundary or reliability data.
/// </summary>
[Serializable]
public class RoomFrame
{
    public Vector3 originOffset;       // hip-center position in reference-camera frame at capture
    public float   yawDegrees;         // player's facing direction at capture
    // Floor plane stored as components because UnityEngine.Plane is not JsonUtility-serializable.
    // Default: normal=(0,1,0) (up), distance = -floorHeight from the legacy RoomCalibration.
    public float   floorNormalX;
    public float   floorNormalY = 1f;
    public float   floorNormalZ;
    public float   floorPlaneDistance; // = -floorHeight; plane eq: dot(normal, p) + distance = 0
    public float   scale = 1f;         // room→game uniform scale factor

    public Plane floorPlane =>
        new Plane(new Vector3(floorNormalX, floorNormalY, floorNormalZ), floorPlaneDistance);

    /// <summary>
    /// Matrix that maps a reference-camera-frame position into game space.
    /// Matches RoomCalibration.GetRoomToGame() exactly.
    /// </summary>
    public Matrix4x4 GetRoomToGame()
    {
        // floorHeight in the legacy model = -floorPlaneDistance (plane normal is up, dist = -height)
        float floorHeight = -floorPlaneDistance;
        Matrix4x4 center = Matrix4x4.Translate(new Vector3(-originOffset.x, -floorHeight, -originOffset.z));
        Matrix4x4 rotate  = Matrix4x4.Rotate(Quaternion.Euler(0f, -yawDegrees, 0f));
        Matrix4x4 scaleM  = Matrix4x4.Scale(Vector3.one * scale);
        return scaleM * rotate * center;
    }
}
