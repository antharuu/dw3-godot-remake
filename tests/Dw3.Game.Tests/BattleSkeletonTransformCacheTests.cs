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

    /// <summary>
    /// Verifies that retained matrices apply nonuniform scale in bone-local axes after rotation,
    /// while a locally hidden parent retains its previous matrix for its visible child.
    /// </summary>
    [Fact]
    public void HiddenParentChildRetainsRotationTimesLocalScaleUnderNonIdentityRoot()
    {
        var transforms = new BattleSkeletonTransformCache(new Dictionary<int, int?> { [1] = null, [2] = 1 });
        var root = new Transform3D(new Basis(Vector3.Up, Mathf.Pi / 4f), new Vector3(3, -2, 5));
        var parent = BattleBoneTransform.Compose(new Basis(Vector3.Back, Mathf.Pi / 2f), Vector3.Zero, new Vector3(2, 1, 1));
        var child = BattleBoneTransform.Compose(Basis.Identity, Vector3.Right, Vector3.One);
        transforms.Apply(1, parent, visible: true);
        transforms.Apply(2, child, visible: true);
        transforms.Apply(1, Transform3D.Identity.ScaledLocal(Vector3.Zero), visible: false);
        transforms.Apply(2, child, visible: true);

        var expectedParent = new Transform3D(new Basis(Vector3.Back, Mathf.Pi / 2f).ScaledLocal(new Vector3(2, 1, 1)), Vector3.Zero);
        var expectedChild = new Transform3D(Basis.Identity, Vector3.Right);
        var expected = root * expectedParent * expectedChild;
        var actual = root * transforms.World(2);
        Assert.True((actual * Vector3.Right).DistanceTo(expected * Vector3.Right) < 0.0001f);
    }
}
