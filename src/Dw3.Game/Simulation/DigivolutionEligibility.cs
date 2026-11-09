using Dw3Production.Conversion;

namespace Dw3Production.Simulation;

/// <summary>Evaluates the extracted ownership, prerequisite and stat gates for normal Digivolution forms.</summary>
public static class DigivolutionEligibility
{
    /// <summary>Returns owned target forms whose source requirements are met by the persistent partner state.</summary>
    /// <param name="partner">Source evolution tree for the session partner.</param>
    /// <param name="knownForms">Forms owned by the session partner.</param>
    /// <param name="stats">Current persistent battle totals used by evolution requirements.</param>
    /// <returns>Eligible owned target forms in source tree order.</returns>
    public static IReadOnlyList<int> AvailableForms(PartnerDigivolution partner, IReadOnlyList<int> knownForms, BattleStats stats)
    {
        ArgumentNullException.ThrowIfNull(partner);
        ArgumentNullException.ThrowIfNull(knownForms);
        ArgumentNullException.ThrowIfNull(stats);

        var known = new HashSet<int>(knownForms);
        return [.. partner.Evolutions
            .Where(entry => IsEligible(entry, known, stats))
            .Select(entry => entry.DigimonId)
            .Distinct()];
    }

    /// <summary>Tests ownership, form prerequisites and the entry's stat threshold.</summary>
    /// <param name="entry">One extracted evolution entry.</param>
    /// <param name="knownForms">Owned form ids.</param>
    /// <param name="stats">Persistent partner totals.</param>
    /// <returns>Whether the entry can be selected.</returns>
    private static bool IsEligible(DigivolutionEntry entry, HashSet<int> knownForms, BattleStats stats)
    {
        return entry.DigimonId != 0
            && knownForms.Contains(entry.DigimonId)
            && HasPrerequisites(entry.Needs, knownForms, stats.Level)
            && MeetsStatRequirement(entry, stats);
    }

    /// <summary>Tests every prerequisite without treating an empty source slot as a required form.</summary>
    /// <param name="needs">Source prerequisites.</param>
    /// <param name="knownForms">Owned form ids.</param>
    /// <param name="level">Persistent partner level.</param>
    /// <returns>Whether every non-empty prerequisite is owned at its required level.</returns>
    private static bool HasPrerequisites(IReadOnlyList<EvolutionNeed> needs, HashSet<int> knownForms, int level)
    {
        return needs.All(need => need.DigimonId == 0 || (knownForms.Contains(need.DigimonId) && level >= need.Level));
    }

    /// <summary>Tests the decoded stat selector against the current persistent totals.</summary>
    /// <param name="entry">Source evolution entry.</param>
    /// <param name="stats">Persistent partner totals.</param>
    /// <returns>Whether the selector's threshold is met.</returns>
    private static bool MeetsStatRequirement(DigivolutionEntry entry, BattleStats stats)
    {
        return entry.Stat switch
        {
            0 => true,
            >= 1 and <= 5 => stats.Stats[entry.Stat - 1] >= entry.Value,
            7 => stats.Level >= entry.Value,
            >= 8 and <= 14 => stats.Resist[entry.Stat - 8] >= entry.Value,
            _ => false,
        };
    }
}
