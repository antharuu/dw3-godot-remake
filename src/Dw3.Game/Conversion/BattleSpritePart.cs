namespace Dw3Production.Conversion;

/// <summary>One placed texture part of a decoded sprite frame, in source placement order.</summary>
/// <param name="Path">Absolute path of the prepared part layer image.</param>
/// <param name="Dx">Horizontal placement relative to the sprite pivot, in source pixels.</param>
/// <param name="Dy">Vertical placement relative to the sprite pivot, in source pixels.</param>
/// <param name="Width">Layer width in source pixels.</param>
/// <param name="Height">Layer height in source pixels.</param>
/// <param name="PaletteSource">Qualified palette provider, <c>effect-page</c> or <c>cleared-vram</c>.</param>
public sealed record BattleSpritePart(string Path, int Dx, int Dy, int Width, int Height, string PaletteSource);
