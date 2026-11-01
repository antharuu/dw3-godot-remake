namespace Dw3.Extractor;

/// <summary>One decoded FIELDCOM frame with its placement and blend metadata.</summary>
public sealed record FieldcomExtractedFrame(int FrameId, int Width, int Height, int OffsetX, int OffsetY, int Row, int Abr, byte[] RgbaPixels);
