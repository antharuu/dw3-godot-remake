namespace Dw3.Formats.Field;

/// <summary>One sprite (sub-texture placement) within a tile's depth layer.</summary>
/// <param name="Depth">Depth layer index (0 = back, 1 = mid, 2 = front).</param>
/// <param name="X">Destination x within the 128×128 tile canvas.</param>
/// <param name="Y">Destination y within the 128×128 tile canvas.</param>
/// <param name="U">Source u within the TIM texture.</param>
/// <param name="V">Source v within the TIM texture.</param>
/// <param name="W">Sprite width in pixels.</param>
/// <param name="H">Sprite height in pixels.</param>
public sealed record ZoneSpriteRecord(int Depth, int X, int Y, int U, int V, int W, int H);
