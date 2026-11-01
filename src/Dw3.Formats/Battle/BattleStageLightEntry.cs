namespace Dw3.Formats.Battle;

/// <summary>One light source within a battle stage.</summary>
/// <param name="DirX">X component of the light direction vector (PS1 s32).</param>
/// <param name="DirY">Y component.</param>
/// <param name="DirZ">Z component.</param>
/// <param name="R">Red channel (0-255).</param>
/// <param name="G">Green channel.</param>
/// <param name="B">Blue channel.</param>
public sealed record BattleStageLightEntry(int DirX, int DirY, int DirZ, byte R, byte G, byte B);
