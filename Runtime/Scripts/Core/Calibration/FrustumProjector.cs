using UnityEngine;

/// <summary>
/// Projects a camera's view frustum corners onto a floor plane to produce
/// a ground-plane footprint polygon. Used to compute the trackable region
/// analytically from camera intrinsics without requiring a boundary walk.
/// </summary>
public static class FrustumProjector
{
    /// <summary>
    /// Compute the floor footprint of a camera's frustum.
    /// </summary>
    /// <param name="K">3×3 intrinsic matrix packed into a Matrix4x4 (K[0,0]=fx, K[1,1]=fy, K[0,2]=cx, K[1,2]=cy).</param>
    /// <param name="cameraToWorld">Camera-to-world transform (use Matrix4x4.identity when camera IS the world origin).</param>
    /// <param name="imageWidth">Image width in pixels.</param>
    /// <param name="imageHeight">Image height in pixels.</param>
    /// <param name="floorPlane">Floor plane in world coordinates.</param>
    /// <param name="maxDistance">Clamp rays to this distance to prevent shallow-angle runaway.</param>
    /// <returns>4 world-space points (TL, TR, BR, BL) or null if all rays are degenerate.</returns>
    public static Vector3[] ProjectFloorFootprint(
        Matrix4x4 K, Matrix4x4 cameraToWorld,
        int imageWidth, int imageHeight,
        Plane floorPlane, float maxDistance = 6f)
    {
        float fx = K[0, 0];
        float fy = K[1, 1];
        float cx = K[0, 2];
        float cy = K[1, 2];

        if (fx == 0f || fy == 0f) return null;

        Vector3 cameraPos = cameraToWorld.GetColumn(3);

        // Image corners in TL, TR, BR, BL order.
        Vector2[] corners = {
            new Vector2(0,          0),
            new Vector2(imageWidth, 0),
            new Vector2(imageWidth, imageHeight),
            new Vector2(0,          imageHeight),
        };

        Vector3[] result = new Vector3[4];
        for (int i = 0; i < 4; i++)
        {
            float u = corners[i].x;
            float v = corners[i].y;

            // Unproject pixel to a direction in camera space via K_inv.
            Vector3 dirCam = new Vector3((u - cx) / fx, (v - cy) / fy, 1f);

            // Rotate into world space (MultiplyVector ignores translation).
            Vector3 dirWorld = cameraToWorld.MultiplyVector(dirCam).normalized;

            Ray ray = new Ray(cameraPos, dirWorld);

            if (floorPlane.Raycast(ray, out float dist) && dist > 0f && dist <= maxDistance)
            {
                result[i] = ray.GetPoint(dist);
            }
            else
            {
                // Ray is parallel to floor, behind camera, or beyond maxDistance.
                // Project the max-distance endpoint onto the floor plane as a conservative bound.
                Vector3 farPoint = cameraPos + dirWorld * maxDistance;
                result[i] = floorPlane.ClosestPointOnPlane(farPoint);
            }
        }

        return result;
    }
}
