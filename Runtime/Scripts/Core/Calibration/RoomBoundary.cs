using System;
using UnityEngine;

/// <summary>
/// The play space footprint: a trackable polygon plus a derived axis-aligned rectangle
/// that games consume for world layout. Coordinates are game-space XZ (Y is up, dropped).
/// </summary>
[Serializable]
public class RoomBoundary
{
    public Vector2[] trackable;       // game-space XZ polygon; analytical or walked
    public Rect      playRect;        // largest inscribed rect; center at RoomBoundary.center, rotated by longAxisDegrees
    public Vector2   center;          // center of playRect in game-space XZ; game spawn point
    public float     longAxisDegrees; // rotation of playRect around center (degrees from game +X)

    public bool HasTrackable => trackable != null && trackable.Length >= 3;

    /// <summary>
    /// Find the largest oriented rectangle inscribed within the given polygon.
    /// Searches rotation angles 0–85° in 5° steps; for each, scans widths and
    /// binary-searches for max height. Returns the rect (width/height + centroid position)
    /// and the rotation angle in degrees.
    /// </summary>
    public static (Rect rect, float angleDegrees) ComputeInscribedRect(Vector2[] polygon)
    {
        if (polygon == null || polygon.Length < 3) return (Rect.zero, 0f);

        Vector2 centroid = Centroid2D(polygon);

        float bestArea = 0f, bestW = 0f, bestH = 0f;
        float bestAngle = 0f;

        for (int deg = 0; deg < 90; deg += 5)
        {
            float rad = deg * Mathf.Deg2Rad;
            Vector2[] rot = Rotate2D(polygon, centroid, -rad);

            // Bounding box of rotated polygon.
            float minX = rot[0].x, maxX = rot[0].x, minY = rot[0].y, maxY = rot[0].y;
            foreach (var p in rot)
            {
                if (p.x < minX) minX = p.x; if (p.x > maxX) maxX = p.x;
                if (p.y < minY) minY = p.y; if (p.y > maxY) maxY = p.y;
            }
            float bboxW = maxX - minX;
            float bboxH = maxY - minY;
            // Rotation is around centroid, so centroid coords are unchanged in rotated frame.
            float cx = centroid.x, cy = centroid.y;

            // For each candidate width, binary-search for the tallest fitting height.
            const float step = 0.05f;
            for (float w = step; w <= bboxW + 0.001f; w += step)
            {
                float clampedW = Mathf.Min(w, bboxW);
                float lo = 0f, hi = bboxH;
                for (int iter = 0; iter < 16; iter++)
                {
                    float mid = (lo + hi) * 0.5f;
                    if (Rect4CornersInPolygon(cx, cy, clampedW, mid, rot)) lo = mid;
                    else hi = mid;
                }
                float area = clampedW * lo;
                if (area > bestArea) { bestArea = area; bestW = clampedW; bestH = lo; bestAngle = deg; }
            }
        }

        // Center is the polygon centroid (the rectangle is centered there).
        Rect result = new Rect(centroid.x - bestW * 0.5f, centroid.y - bestH * 0.5f, bestW, bestH);
        return (result, bestAngle);
    }

    private static bool Rect4CornersInPolygon(float cx, float cy, float w, float h, Vector2[] poly)
    {
        float hw = w * 0.5f, hh = h * 0.5f;
        return PointInPolygon(new Vector2(cx - hw, cy - hh), poly)
            && PointInPolygon(new Vector2(cx + hw, cy - hh), poly)
            && PointInPolygon(new Vector2(cx + hw, cy + hh), poly)
            && PointInPolygon(new Vector2(cx - hw, cy + hh), poly);
    }

    private static bool PointInPolygon(Vector2 p, Vector2[] poly)
    {
        bool inside = false;
        int n = poly.Length;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            Vector2 a = poly[i], b = poly[j];
            if ((a.y > p.y) != (b.y > p.y) &&
                p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x)
                inside = !inside;
        }
        return inside;
    }

    private static Vector2[] Rotate2D(Vector2[] pts, Vector2 origin, float rad)
    {
        float c = Mathf.Cos(rad), s = Mathf.Sin(rad);
        var r = new Vector2[pts.Length];
        for (int i = 0; i < pts.Length; i++)
        {
            float dx = pts[i].x - origin.x, dy = pts[i].y - origin.y;
            r[i] = new Vector2(origin.x + c * dx - s * dy, origin.y + s * dx + c * dy);
        }
        return r;
    }

    private static Vector2 Centroid2D(Vector2[] pts)
    {
        var sum = Vector2.zero;
        foreach (var p in pts) sum += p;
        return sum / pts.Length;
    }
}
