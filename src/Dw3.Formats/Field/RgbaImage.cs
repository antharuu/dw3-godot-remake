namespace Dw3.Formats.Field;

/// <summary>An RGBA image used only inside the field decoder.</summary>
public sealed record RgbaImage(int Width, int Height, byte[] Pixels);
