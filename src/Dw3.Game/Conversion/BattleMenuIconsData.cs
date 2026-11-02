namespace Dw3Production.Conversion;

/// <summary>CMFONTCS icons used by Technique/Item plus the battle-message layout contract's five-row wait arrow.</summary>
public sealed record BattleMenuIconsData(IReadOnlyDictionary<int, BattleMenuIconFrame> Frames, int ArrowFrame, int ArrowX, int ArrowY, int ArrowTimeStep);
