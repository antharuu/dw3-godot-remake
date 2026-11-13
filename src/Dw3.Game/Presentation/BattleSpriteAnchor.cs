namespace Dw3Production.Presentation;

/// <summary>
/// The position a script gives a sprite effect: a battle-world point that the source projects with
/// the battle camera, or a source screen point when its depth is <c>0x7FFF</c> (front) or <c>-1</c> (back).
/// </summary>
/// <param name="X">World or source-screen X.</param>
/// <param name="Y">World or source-screen Y.</param>
/// <param name="Z">World Z, or one of the two screen markers.</param>
internal readonly record struct BattleSpriteAnchor(short X, short Y, short Z)
{
    /// <summary>The screen marker drawn at the front of its layer (ordering-table entry zero).</summary>
    public const short FrontScreenMarker = 0x7FFF;

    /// <summary>The screen marker drawn at the back of its layer (ordering-table entry <c>0xFFF</c>).</summary>
    public const short BackScreenMarker = -1;

    /// <summary>Gets a value indicating whether the anchor is a source screen position rather than a projected point.</summary>
    public bool IsScreen => Z is FrontScreenMarker or BackScreenMarker;
}
