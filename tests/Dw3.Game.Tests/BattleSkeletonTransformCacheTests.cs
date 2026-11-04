using Dw3Production.Presentation;
using Godot;
using Xunit;

namespace Dw3.Game.Tests;

/// <summary>
/// Pins the source-style retained world transforms used when a parent bone becomes locally hidden.
/// </summary>
public sealed class BattleSkeletonTransformCacheTests
{
    /// <summary>
    /// Verifies that a visible child composes with its parent's last visible world transform rather
    /// than a zero-scale pose which the source skips while that parent is hidden.
    /// </summary>
    [Fact]
    public void HiddenParentKeepsItsLastWorldTransformForVisibleChildren()
    {
        var transforms = new BattleSkeletonTransformCache(new Dictionary<int, int?>
        {
            [0] = null,
            [1] = 0,
            [2] = 1,
        });

        transforms.Apply(1, Transform3D.Identity.Translated(new Vector3(4, 0, 0)), visible: true);
        transforms.Apply(2, Transform3D.Identity.Translated(new Vector3(1, 0, 0)), visible: true);
        transforms.Apply(1, Transform3D.Identity.Scaled(Vector3.Zero), visible: false);
        transforms.Apply(2, Transform3D.Identity.Translated(new Vector3(2, 0, 0)), visible: true);

        Assert.Equal(new Vector3(4, 0, 0), transforms.World(1).Origin);
        Assert.Equal(new Vector3(6, 0, 0), transforms.World(2).Origin);
    }

    /// <summary>Verifies that a zero scale component selects the target scale without interpolation.</summary>
    [Theory]
    [InlineData(372)]
    [InlineData(2048)]
    [InlineData(3723)]
    public void ZeroScaleBlendSelectsTheTargetAtEveryWeight(int weight)
    {
        var source = new Vector3(4096, 4096, 4096);
        Assert.Equal(Vector3.Zero, BattleSkeleton.BlendScale(source, Vector3.Zero, weight));
    }
}
