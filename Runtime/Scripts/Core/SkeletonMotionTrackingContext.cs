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
}
