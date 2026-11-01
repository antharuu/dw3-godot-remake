namespace Dw3.Formats.Battle;

/// <summary>A row-major RGBA8 image buffer of <paramref name="Width"/> × <paramref name="Height"/> pixels.</summary>
/// <param name="Pixels">Four bytes per pixel (R, G, B, A); length is Width × Height × 4.</param>
/// <param name="Width">Width in pixels.</param>
/// <param name="Height">Height in pixels.</param>
public readonly record struct HudRgbaImage(byte[] Pixels, int Width, int Height);
