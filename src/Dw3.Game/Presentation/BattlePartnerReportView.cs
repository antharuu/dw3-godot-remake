using Dw3Production.Conversion;

namespace Dw3Production.Presentation;

/// <summary>One partner's row in the victory report: what exp it gained, its level after the
/// growth, and whether it levelled up.</summary>
public readonly record struct BattlePartnerReportView(int Id, BattleTextLine? Name, int ExpGained, int Level, bool LeveledUp);
