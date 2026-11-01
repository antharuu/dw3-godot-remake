namespace Dw3.Extractor;

/// <summary>One decoded TIM frame from a player action archive.</summary>
public sealed record ActionFrame(int FrameId, int Width, int Height, byte[] RgbaPixels);
