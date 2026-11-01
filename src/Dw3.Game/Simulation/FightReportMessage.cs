namespace Dw3Production.Simulation;

/// <summary>Describes the message currently displayed by the battle report.</summary>
public sealed record FightReportMessage(
    int Entry,
    IReadOnlyList<int> Substitutions,
    int TokensRevealed,
    int TokenCount,
    bool ArrowVisible,
    int ArrowFrame);
