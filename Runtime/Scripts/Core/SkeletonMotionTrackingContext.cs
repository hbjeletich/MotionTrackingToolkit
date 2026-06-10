using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SkeletonMotionTrackingContext : IMotionTrackingManager
{
    private MultiplayerMotionTrackingManager multiplayerManager;
    private int skeletonId;

    public MotionTrackingConfiguration Config => multiplayerManager?.Config;
    public MotionSource Source => multiplayerManager?.Source ?? MotionSource.Custom;

    public SkeletonMotionTrackingContext(MultiplayerMotionTrackingManager manager, int skeletonId)
    {
        this.multiplayerManager = manager;
        this.skeletonId = skeletonId;
    }

    public Transform GetJointByName(string jointName)
    {
        // call to the multiplayer manager with the specific skeleton ID
        return multiplayerManager?.GetJointByName(skeletonId, jointName);
    }

    public void LoadConfiguration(MotionTrackingConfiguration config) =>
        multiplayerManager?.LoadConfiguration(config);

    public void Recalibrate() => multiplayerManager?.RecalibrateSkeleton(skeletonId);

    public void SaveCalibration(string calibrationName)
    {
        if (multiplayerManager == null) return;
        if (multiplayerManager.TryGetSkeletonById(skeletonId, out int playerNumber, out _))
            multiplayerManager.SaveCalibration(playerNumber, calibrationName);
    }

    public bool LoadCalibration(string calibrationName)
    {
        if (multiplayerManager == null) return false;
        if (multiplayerManager.TryGetSkeletonById(skeletonId, out int playerNumber, out _))
            return multiplayerManager.LoadCalibration(playerNumber, calibrationName);
        return false;
    }

    public bool SupportsRoomScale => multiplayerManager?.SupportsRoomScale ?? false;

    public bool TryGetRoomPosition(out Vector3 gamePosition)
    {
        if (multiplayerManager != null) return multiplayerManager.TryGetRoomPosition(out gamePosition);
        gamePosition = Vector3.zero;
        return false;
    }

    public bool HasRoomBounds => multiplayerManager?.HasRoomBounds ?? false;

    public Vector3[] GetRoomBoundary() => multiplayerManager?.GetRoomBoundary() ?? System.Array.Empty<Vector3>();

    public float RoomMinTrackingDistance => multiplayerManager?.RoomMinTrackingDistance ?? 0f;
}
