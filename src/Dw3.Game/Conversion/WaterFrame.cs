namespace Dw3Production.Conversion;

/// <summary>One frame of an animated map object: its opaque and STP image layers.</summary>
public sealed record WaterFrame(string OpaquePath, string SemiPath, int OffsetX, int OffsetY, int Abr);
