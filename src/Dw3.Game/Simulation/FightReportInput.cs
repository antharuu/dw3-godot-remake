namespace Dw3Production.Simulation;

/// <summary>Supplies the rewards and party data displayed by a battle report.</summary>
public sealed record FightReportInput(
    IReadOnlyList<FightReportPartner> Partners,
    int Money,
    int Item,
    IReadOnlyDictionary<int, int>? MessageTokenCounts = null);
