namespace Dw3.Formats.Battle;

/// <summary>
/// One decoded PS1 texture page, cut from a battle model's local VRAM and converted to RGBA.
/// Tpage mode (0 = 4-bit, 1 = 8-bit, 2 = 16-bit) determines how the polygon UVs index this texture.
/// </summary>
/// <param name="Key">Zero-based texture key assigned during decoding; used by polygons to reference this page.</param>
/// <param name="Mode">PS1 tpage depth: 0 = 4-bit indexed, 1 = 8-bit indexed, 2 = 16-bit direct.</param>
/// <param name="Page">PS1 tpage X page index (each page is 64 halfwords wide in VRAM).</param>
/// <param name="ClutX">CLUT X position in VRAM halfwords for indexed modes; null for 16-bit direct.</param>
/// <param name="ClutY">CLUT Y row in VRAM for indexed modes; null for 16-bit direct.</param>
/// <param name="Width">Decoded texture width in pixels.</param>
/// <param name="Height">Decoded texture height in pixels.</param>
/// <param name="TransparentPixels">Number of fully transparent pixels (alpha 0) in this texture.</param>
/// <param name="StpPixels">Number of STP-flagged (semi-transparent) pixels in this texture.</param>
/// <param name="Rgba">RGBA pixel data, 4 bytes per pixel, row-major; length is exactly Width × Height × 4.</param>
public sealed record BattleTexture(
    int Key, int Mode, int Page, int? ClutX, int? ClutY, int Width, int Height,
    int TransparentPixels, int StpPixels, byte[] Rgba);
