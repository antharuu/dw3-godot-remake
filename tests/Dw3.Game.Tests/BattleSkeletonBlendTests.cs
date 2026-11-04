using Dw3Production.Presentation;
using Godot;
using Xunit;

namespace Dw3.Game.Tests;

/// <summary>
/// Pins the fixed-origin state used by battle-motion interpolation with fabricated poses.
/// </summary>
public sealed class BattleSkeletonBlendTests
{
    /// <summary>
    /// Verifies that publishing an intermediate blend pose does not replace the source retained for
    /// the remaining entries addressed to the same target.
    /// </summary>
    [Fact]
    public void OriginSurvivesIntermediatePublishedBlendPose()
    {
        var poses = new BattleBlendState();
        var source = new BattleBlendPose(Vector3.Zero, new Vector3(-40, 10, -2), new Vector3(4096, 4096, 4096));
        var target = new BattleBlendPose(Vector3.Zero, new Vector3(-160, 46, -10), new Vector3(4096, 4096, 4096));
        poses.Publish(0, source);
        poses.CaptureOrigin(0);
        poses.Publish(0, new BattleBlendPose(Vector3.Zero, new Vector3(-100, 28, -6), new Vector3(4096, 4096, 4096)));

        Assert.Equal(source, poses.Origin(0));
        Assert.Equal(new Vector3(-100, 28, -6), BattleKeyTrack.FixedBlend(poses.Origin(0).Rotation, target.Rotation, 2048));
    }

    /// <summary>
    /// Verifies that a fresh capture replaces the fixed source when the motion selects another
    /// blend target after publishing a completed pose.
    /// </summary>
    [Fact]
    public void NewTargetCapturesTheCurrentPublishedPose()
    {
        var poses = new BattleBlendState();
        var first = new BattleBlendPose(new Vector3(1, 2, 3), Vector3.Zero, new Vector3(4096, 4096, 4096));
        var next = new BattleBlendPose(new Vector3(4, 5, 6), Vector3.Zero, new Vector3(4096, 4096, 4096));
        poses.Publish(0, first);
        poses.CaptureOrigin(0);
        poses.Publish(0, next);
        poses.CaptureOrigin(0);

        Assert.Equal(next, poses.Origin(0));
    }
}
