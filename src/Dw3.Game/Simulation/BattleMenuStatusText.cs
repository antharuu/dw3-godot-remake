using Dw3Production.Conversion;

namespace Dw3Production.Simulation;

/// <summary>Source MFIGHT labels for the Technique and Item status windows.</summary>
public sealed record BattleMenuStatusText(BattleTextLine Header, BattleTextLine? CurrentOverride = null,
    BattleTextLine? Separator = null);
