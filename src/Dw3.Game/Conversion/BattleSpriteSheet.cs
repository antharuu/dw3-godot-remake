namespace Dw3Production.Conversion;

/// <summary>
/// A prepared, renderable two-dimensional effect: the animations the source effect task starts
/// (archive entries up to the first empty one, at most thirty) and every frame they reference.
/// </summary>
/// <param name="Id">Script-visible sprite effect identity.</param>
/// <param name="Animations">Child animations in archive order, each a list of source-time samples.</param>
/// <param name="Frames">Decoded frames keyed by frame and animated CLUT row.</param>
public sealed record BattleSpriteSheet(int Id, IReadOnlyList<IReadOnlyList<BattleSpriteSample>> Animations, IReadOnlyDictionary<(int Frame, int Row), BattleSpriteFrame> Frames)
{
    /// <summary>The source sprite-effect task creates at most this many child animations.</summary>
    public const int MaxAnimations = 30;

    /// <summary>Gets the decoded frame referenced by a sample.</summary>
    /// <param name="sample">A sample of one of this sheet's animations.</param>
    /// <returns>The frame and palette image contract.</returns>
    /// <exception cref="KeyNotFoundException">The sample does not belong to this sheet.</exception>
    public BattleSpriteFrame FrameOf(BattleSpriteSample sample) => Frames[sample.Key];
}
