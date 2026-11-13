namespace Dw3Production.Conversion;

/// <summary>One decoded frame and animated palette of a sprite sheet.</summary>
/// <param name="Frame">Source frame identity.</param>
/// <param name="Row">Animated CLUT row.</param>
/// <param name="Abr">Source blend mode: -1 opaque, 0 to 3 the PS1 semi-transparency modes.</param>
/// <param name="Parts">Placed layers in source placement order (placement zero is drawn first).</param>
public sealed record BattleSpriteFrame(int Frame, int Row, int Abr, IReadOnlyList<BattleSpritePart> Parts)
{
    /// <summary>Gets a value indicating whether STP texels of this frame are blended.</summary>
    public bool IsSemiTransparent => Abr != -1;
}
