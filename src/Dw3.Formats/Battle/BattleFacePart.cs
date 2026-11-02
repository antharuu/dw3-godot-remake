namespace Dw3.Formats.Battle;

/// <summary>
/// One sprite region within the face animation atlas for a battle fighter, decoded from the
/// fighter's metadata block. A part covers a rectangular sub-region of the fighter's face
/// sprite sheet and cycles through one or more <see cref="BattleFaceFrame"/> positions.
/// </summary>
/// <param name="Index">Zero-based part index within the face definition.</param>
/// <param name="X">Left pixel coordinate of the part rectangle on the atlas.</param>
/// <param name="Y">Top pixel coordinate of the part rectangle on the atlas.</param>
/// <param name="Width">Width of the part rectangle in pixels.</param>
/// <param name="Height">Height of the part rectangle in pixels.</param>
/// <param name="Frames">Animation frame positions for this part, in playback order.</param>
public sealed record BattleFacePart(int Index, int X, int Y, int Width, int Height, IReadOnlyList<BattleFaceFrame> Frames);
