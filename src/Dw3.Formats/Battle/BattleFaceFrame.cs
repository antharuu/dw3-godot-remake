namespace Dw3.Formats.Battle;

/// <summary>
/// One atlas frame position for a face animation part, decoded from the fighter's metadata block.
/// X and Y are pixel offsets into the face sprite atlas that hold this frame's image.
/// </summary>
/// <param name="X">Horizontal offset of this frame within the face sprite atlas, in pixels.</param>
/// <param name="Y">Vertical offset of this frame within the face sprite atlas, in pixels.</param>
public sealed record BattleFaceFrame(int X, int Y);
