using UnityEngine;

/// <summary>
/// Scene-view overlay for room calibration debug. Shows the trackable region
/// polygon (green) and player position (cyan). Attach to any GameObject.
/// </summary>
[ExecuteAlways]
public class RoomCalibrationDebugDrawer : MonoBehaviour
{
    [SerializeField] private Color regionColor = Color.green;
    [SerializeField] private Color playerColor = Color.cyan;
    [SerializeField] private float drawHeight   = 0f;

    void OnDrawGizmos()
    {
        var orch = MotionTrackingOrchestrator.Instance;
        if (orch == null) return;

        var regionProvider = orch.ActiveManager as ITrackableRegionProvider;
        if (regionProvider != null && regionProvider.HasTrackableRegion)
        {
            var pts = regionProvider.GetTrackableRegion();
            if (pts != null && pts.Length >= 3)
            {
                Gizmos.color = regionColor;
                for (int i = 0; i < pts.Length; i++)
                {
                    int j = (i + 1) % pts.Length;
                    Gizmos.DrawLine(
                        new Vector3(pts[i].x, drawHeight, pts[i].y),
                        new Vector3(pts[j].x, drawHeight, pts[j].y));
                }

                Gizmos.color = Color.yellow;
                foreach (var p in pts)
                    Gizmos.DrawWireSphere(new Vector3(p.x, drawHeight, p.y), 0.06f);
            }
        }

        if (orch.TryGetRoomPosition(out Vector3 pos))
        {
            Gizmos.color = playerColor;
            Gizmos.DrawSphere(new Vector3(pos.x, drawHeight, pos.z), 0.1f);
        }

        var mpMgr = orch.ActiveManager as MediaPipeMotionTrackingManager;
        var boundary = mpMgr?.ActiveRoomBoundary;
        if (boundary != null && boundary.HasTrackable)
        {
            float rad = boundary.longAxisDegrees * Mathf.Deg2Rad;
            float c = Mathf.Cos(rad), s = Mathf.Sin(rad);
            Vector3 ctr  = new Vector3(boundary.center.x, drawHeight, boundary.center.y);
            Vector3 axX  = new Vector3(c,  0f, s) * (boundary.playRect.width  * 0.5f);
            Vector3 axZ  = new Vector3(-s, 0f, c) * (boundary.playRect.height * 0.5f);
            Vector3 p1 = ctr + axX + axZ, p2 = ctr - axX + axZ;
            Vector3 p3 = ctr - axX - axZ, p4 = ctr + axX - axZ;
            Gizmos.color = Color.magenta;
            Gizmos.DrawLine(p1, p2); Gizmos.DrawLine(p2, p3);
            Gizmos.DrawLine(p3, p4); Gizmos.DrawLine(p4, p1);
            Gizmos.DrawWireSphere(ctr, 0.08f);
        }
    }
}
