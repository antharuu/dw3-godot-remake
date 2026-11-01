using Dw3Production.Conversion;

namespace Dw3Production.Simulation;

/// <summary>One line of the battle menu: its label and whether it can be confirmed.</summary>
public sealed record BattleMenuLine(
    string Label,
    bool Enabled,
    BattleTextLine? Text = null,
    int Icon = -1,
    int Cost = -1,
    int Quantity = -1,
    string Description = "",
    BattleTextLine? DescriptionText = null,
    BattleTextLine? CostLabelText = null);
