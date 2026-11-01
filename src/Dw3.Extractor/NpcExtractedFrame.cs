namespace Dw3.Extractor;

/// <summary>One decoded TIM4 frame from a character archive.</summary>
public sealed record NpcExtractedFrame(int FrameId, int Width, int Height, byte[] RgbaPixels);
