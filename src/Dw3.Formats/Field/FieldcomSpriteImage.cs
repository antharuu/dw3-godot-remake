namespace Dw3.Formats.Field;

/// <summary>One FIELDCOM sprite composed in PS1 word colours.</summary>
public sealed record FieldcomSpriteImage(int Width, int Height, int OffsetX, int OffsetY, int Abr, ushort[] Words);
