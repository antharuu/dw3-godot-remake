namespace Dw3.Formats.Battle;

/// <summary>
/// One part entry from a SpriteDrawer sprite sheet (WFIGHTMN / CMFONTCS / STFGTREP).
/// Sourced from the parts table at the offset read from the sheet header.
/// 7 × s16 = 14 bytes per entry.
/// </summary>
/// <param name="U">Texture U coordinate (texels; for 4-bit: (u // 4) gives the halfword column).</param>
/// <param name="V">Texture V coordinate (rows).</param>
/// <param name="Width">Width in texels.</param>
/// <param name="Height">Height in texels.</param>
/// <param name="ClutX">CLUT X position in halfword units within the texture page.</param>
/// <param name="ClutY">Base CLUT Y row.</param>
/// <param name="Mode">Colour mode: 0 = 4-bit (the only valid value for these sheets).</param>
public sealed record HudSpritePart(short U, short V, short Width, short Height, short ClutX, short ClutY, short Mode);
