namespace Dw3Production.Conversion;

/// <summary>One talk-box frame sprite composited to a standalone RGBA PNG. Frames: 0 top edge,
/// 2 bottom edge, 3/4/5/6 the four corner sprites, 7 the "next" arrow (<see cref="ClutFrames"/>
/// blink variants). <see cref="Semi"/> is true when the sheet frame is drawn semi-transparent.</summary>
public sealed record DialogueFrame(int Id, string Path, int W, int H, int ClutFrames, bool Semi);
