using Dw3Production.Conversion;
using Dw3Production.Session;

namespace Dw3Production.Simulation;

/// <summary>Unlocks the forms of a partner's decoded evolution list whose requirements are met.</summary>
public static class FormUnlockRules
{
    /// <summary>Repeatedly acquires the first listed form whose prerequisites are met, filling free slots.</summary>
    /// <param name="partner">Persistent partner state.</param>
    /// <param name="evolutions">Ordered decoded evolution list.</param>
    /// <returns>New form ids in list order.</returns>
    public static IReadOnlyList<int> LearnForms(PlayerSessionPartner partner, PartnerDigivolution evolutions)
    {
        ArgumentNullException.ThrowIfNull(partner);
        ArgumentNullException.ThrowIfNull(evolutions);
        var result = new List<int>();
        DigivolutionEntry? next;
        while ((next = evolutions.Evolutions.FirstOrDefault(entry => Eligible(partner, entry))) is not null)
        {
            partner.AcquireForm(next.DigimonId);
            result.Add(next.DigimonId);
        }

        return result;
    }

    /// <summary>Checks a decoded form requirement against acquired form levels and partner growth.</summary>
    /// <param name="partner">Persistent partner state.</param>
    /// <param name="entry">Candidate form requirement.</param>
    /// <returns>True when the form can be acquired.</returns>
    private static bool Eligible(PlayerSessionPartner partner, DigivolutionEntry entry) =>
        entry.DigimonId > 0
        && !partner.KnownForms.Contains(entry.DigimonId)
        && entry.Needs.All(need => need.DigimonId == 0 || (partner.KnownForms.Contains(need.DigimonId) && partner.FormProgress(need.DigimonId).Level >= need.Level))
        && StatMet(partner.Growth, entry.Stat, entry.Value);

    /// <summary>Checks the one optional stat gate.</summary>
    /// <param name="growth">Current partner growth.</param>
    /// <param name="stat">Decoded stat selector.</param>
    /// <param name="value">Required minimum.</param>
    /// <returns>True when the gate is absent or met.</returns>
    private static bool StatMet(PartnerGrowthState? growth, int stat, int value)
    {
        if (stat == 0)
        {
            return true;
        }

        if (growth is null)
        {
            return false;
        }

        return stat switch
        {
            < 7 => stat > 0 && stat - 1 < growth.BattleStats.Length && growth.BattleStats[stat - 1] >= value,
            7 => growth.Level >= value,
            _ => stat - 8 < growth.Resistances.Length && growth.Resistances[stat - 8] >= value,
        };
    }
}
