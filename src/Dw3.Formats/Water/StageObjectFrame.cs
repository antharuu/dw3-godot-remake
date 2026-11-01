namespace Dw3.Formats.Water;

/// <summary>One rendered frame of a map object at one palette row.</summary>
/// <param name="FrameId">Sprite-sheet frame id.</param>
/// <param name="ClutRow">Palette row shift used for this rendering.</param>
/// <param name="Image">Composed colour words.</param>
public sealed record StageObjectFrame(int FrameId, int ClutRow, SpriteFrameImage Image);
