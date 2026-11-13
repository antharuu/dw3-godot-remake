using Dw3Production.Conversion;

namespace Dw3Production.Simulation;

/// <summary>Groups battle and growth state required to resolve a victory reward.</summary>
public sealed class BattleOutcomeRequest
{
    /// <summary>Gets the terminal battle flow.</summary>
    public required BattleFlowController Flow { get; init; }

    /// <summary>Gets the encounter reward.</summary>
    public required BattleReward Reward { get; init; }

    /// <summary>Gets the encounter id used by the structured reward diagnostic.</summary>
    public int Encounter { get; init; }

    /// <summary>Gets partner growth state, when a test party was initialized.</summary>
    public IReadOnlyList<PartnerGrowthState>? PartyGrowth { get; init; }

    /// <summary>Gets decoded growth tables.</summary>
    public GrowthTables? GrowthTables { get; init; }

    /// <summary>Gets the battle random source used by level-up stat rolls.</summary>
    public required IBattleRng Random { get; init; }

    /// <summary>Gets decoded partner growth metadata for a party slot.</summary>
    public required Func<int, DigimonData> DigimonFor { get; init; }

    /// <summary>Gets decoded display text for a fighter id.</summary>
    public required Func<int, BattleTextLine> NameFor { get; init; }

    /// <summary>Gets a value indicating whether the active partner equipped the source money bonus item.</summary>
    public bool HasMoneyBonusEquipment { get; init; }

    /// <summary>Gets a function that identifies party slots wearing the source experience bonus item.</summary>
    public Func<int, bool> HasExperienceBonusEquipment { get; init; } = _ => false;

    /// <summary>Gets a value indicating whether the encounter forces a scripted prize instead of a random enemy item.</summary>
    public bool HasPrize { get; init; }

    /// <summary>Gets the forced prize item id when <see cref="HasPrize"/> is true.</summary>
    public int Prize { get; init; }

    /// <summary>Gets the battle text catalogue attached to the report.</summary>
    public required BattleTextCatalog Text { get; init; }
}
