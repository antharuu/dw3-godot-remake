namespace Dw3Production.Conversion;

/// <summary>One decoded connection-indicator frame and its draw offset.</summary>
public sealed record PlayerIconFrame(int Frame, string Path, int OffsetX, int OffsetY);
