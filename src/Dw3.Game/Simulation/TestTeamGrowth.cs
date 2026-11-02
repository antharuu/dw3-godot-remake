using Dw3Production.Conversion;

namespace Dw3Production.Simulation;

/// <summary>
/// The debug-only test team: the balanced starter partners at a fixed level, used by the F8
/// battle test and random encounters while no save system exists. Its growth state lives for
/// the session only and is never written anywhere.
/// </summary>
internal static class TestTeamGrowth
{
    /// <summary>Level of every test-team partner.</summary>
    public const int Level = 25;

    /// <summary>
    /// Creates the starting growth state of each partner at <see cref="Level"/>: each stat is its
    /// base value plus its per-level growth times <c>Level - 1</c>, the same stand-in the battle
    /// party uses for its stats, extended to MP and the seven resistances.
    /// </summary>
    /// <param name="digimonIds">Partner Digimon ids, in party order.</param>
    /// <param name="fighters">Fighter table read from the data pack.</param>
    /// <returns>One growth state per partner, in party order.</returns>
    /// <exception cref="KeyNotFoundException">A partner id is missing from the fighter table.</exception>
    public static IReadOnlyList<PartnerGrowthState> Create(IReadOnlyList<int> digimonIds, FighterData fighters)
    {
        var result = new List<PartnerGrowthState>();
        foreach (var id in digimonIds)
        {
            result.Add(CreatePartner(fighters.Digimon[id]));
        }

        return result;
    }

    /// <summary>Grows one partner from its base values to <see cref="Level"/>.</summary>
    /// <param name="digimon">Partner base values and per-level growth.</param>
    /// <returns>The partner growth state.</returns>
    private static PartnerGrowthState CreatePartner(DigimonData digimon)
    {
        const int grown = Level - 1;
        var state = new PartnerGrowthState
        {
            Level = Level,
            Exp = 0,
            MaxHp = digimon.Hp + (digimon.HpGrowth * grown),
            MaxMp = digimon.Mp + (digimon.MpGrowth * grown),
        };
        for (var i = 0; i < state.BattleStats.Length && i < digimon.StatGrowth.Count; i++)
        {
            state.BattleStats[i] = digimon.BattleStats[i] + (digimon.StatGrowth[i] * grown);
        }

        for (var i = 0; i < state.Resistances.Length && i < digimon.ResistGrowth.Count; i++)
        {
            state.Resistances[i] = digimon.Resistances[i] + (digimon.ResistGrowth[i] * grown);
        }

        return state;
    }
}
