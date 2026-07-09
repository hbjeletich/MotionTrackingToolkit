using System;
using UnityEngine;

/// <summary>
/// Continuously computes the player's signed distance to the trackable-region edge and
/// emits an event when the proximity zone changes (Safe / Warning / Danger / Outside).
///
/// Shared source of truth for boundary proximity — consumers (vignette, top-down map, audio
/// cues) should subscribe to OnProximityChanged rather than recomputing signed distance
/// themselves.
///
/// Call Refresh() after recalibration to force re-query of the trackable region.
/// </summary>
public class BoundaryProximityService : MonoBehaviour
{
    public enum Proximity { Safe, Warning, Danger, Outside }

    [Header("Thresholds (metres from edge)")]
    [SerializeField] private float warningDistance = 0.5f;
    [SerializeField] private float dangerDistance = 0.15f;

    public Proximity Current { get; private set; } = Proximity.Safe;
    public float SignedDistanceToEdge { get; private set; }   // positive = inside, negative = outside
    public event Action<Proximity> OnProximityChanged;

    private IRoomFrameSource _frameSource;
    private ITrackableRegionProvider _regionProvider;
    private Vector2[] _trackable;
    private Action<RoomFrame> _onFrameCaptured;

    private void Start()
    {
        var orc = MotionTrackingOrchestrator.Instance;
        _frameSource = orc as IRoomFrameSource;
        _regionProvider = orc as ITrackableRegionProvider;

        if (_frameSource == null || _regionProvider == null)
        {
            Debug.LogWarning("BoundaryProximityService: Orchestrator missing IRoomFrameSource or ITrackableRegionProvider.");
            return;
        }

        _onFrameCaptured = _ => Refresh();
        _frameSource.OnFrameCaptured += _onFrameCaptured;
    }

    private void OnDestroy()
    {
        if (_frameSource != null && _onFrameCaptured != null)
            _frameSource.OnFrameCaptured -= _onFrameCaptured;
    }

    private void Update()
    {
        if (_frameSource == null || _regionProvider == null || !_regionProvider.HasTrackableRegion) return;
        if (!_frameSource.TryGetRoomPosition(out var pos)) return;

        if (_trackable == null)
            _trackable = _regionProvider.GetTrackableRegion();
        if (_trackable == null) return;

        var p2 = new Vector2(pos.x, pos.z);
        SignedDistanceToEdge = SignedDistancePointToPolygon(p2, _trackable);

        var next = SignedDistanceToEdge < 0f            ? Proximity.Outside
                 : SignedDistanceToEdge < dangerDistance  ? Proximity.Danger
                 : SignedDistanceToEdge < warningDistance ? Proximity.Warning
                 : Proximity.Safe;

        if (next != Current)
        {
            Current = next;
            OnProximityChanged?.Invoke(next);
        }
    }

    /// <summary>Force re-query of the trackable region next frame (call after recalibration).</summary>
    public void Refresh() => _trackable = null;

    // Returns positive distance if inside polygon, negative if outside.
    private static float SignedDistancePointToPolygon(Vector2 point, Vector2[] polygon)
    {
        float minDist = float.MaxValue;
        int n = polygon.Length;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            Vector2 ab = polygon[i] - polygon[j];
            float t = ab.sqrMagnitude > 0f
                ? Mathf.Clamp01(Vector2.Dot(point - polygon[j], ab) / ab.sqrMagnitude)
                : 0f;
            minDist = Mathf.Min(minDist, Vector2.Distance(point, polygon[j] + t * ab));
        }
        return PointInPolygon(point, polygon) ? minDist : -minDist;
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
}
