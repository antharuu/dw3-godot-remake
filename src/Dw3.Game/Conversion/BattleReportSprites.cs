namespace Dw3Production.Conversion;

/// <summary>The m6-battle-report-sprites-v1 STFGTREP sheet, partner animation rows and CMFONTCS arrow.</summary>
public sealed record BattleReportSprites(IReadOnlyDictionary<int, BattleHudFrame> Frames, IReadOnlyList<int[]> Animations, BattleHudFrame Arrow);
