namespace Dw3.Formats.Battle;

public sealed record BattleFacePart(int Index, int X, int Y, int Width, int Height, IReadOnlyList<BattleFaceFrame> Frames);
