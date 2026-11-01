namespace Dw3.Formats.Dialogue;

/// <summary>Contains one composited talk-box sprite frame in row-major RGBA order.</summary>
public sealed record TalkBoxFrameImage(int FrameId, int Width, int Height, int ClutFrames, bool Semi, byte[] Rgba);
