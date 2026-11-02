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

    /// <summary>Gets the battle text catalogue attached to the report.</summary>
    public required BattleTextCatalog Text { get; init; }
}
