namespace Dw3.Formats.Battle;

public sealed record BattleFaceSource(int RecordOffset, int FacePointerOffset, int FaceOffset, int RecordSize, string Terminator);
