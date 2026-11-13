using Dw3Production.Conversion;
using Dw3Production.Session;

namespace Dw3Production.Simulation;

/// <summary>Applies the source form-entry experience, skills and evolution requirements after a battle.</summary>
public static class PartnerFormProgression
{
    /// <summary>Applies one battle's Digimon experience to every form used by a partner.</summary>
    /// <param name="partner">Persistent owner of form entries.</param>
    /// <param name="usedForms">Forms marked used in the completed battle.</param>
    /// <param name="digimonExp">Encounter form experience.</param>
    /// <param name="fighters">Decoded form records.</param>
    /// <param name="evolutions">Decoded evolution requirements.</param>
    /// <returns>The newly learned skills and forms.</returns>
    public static FormProgressionResult Apply(
        PlayerSessionPartner partner,
        IReadOnlyCollection<int> usedForms,
        int digimonExp,
        FighterData fighters,
        PartnerDigivolution evolutions)
    {
        ArgumentNullException.ThrowIfNull(partner);
        ArgumentNullException.ThrowIfNull(usedForms);
        ArgumentNullException.ThrowIfNull(fighters);
        ArgumentNullException.ThrowIfNull(evolutions);
        var learnedTechniques = new List<int>();
        foreach (int form in usedForms.Where(partner.KnownForms.Contains))
        {
            ApplyForm(partner, form, digimonExp, fighters, learnedTechniques);
        }

        int[] learnedForms = LearnForms(partner, evolutions);
        return new FormProgressionResult(learnedTechniques, learnedForms);
    }

    /// <summary>Applies experience and source skill thresholds to one acquired form.</summary>
    /// <param name="partner">Persistent form owner.</param>
    /// <param name="form">Used form.</param>
    /// <param name="digimonExp">Encounter form experience.</param>
    /// <param name="fighters">Decoded form records.</param>
    /// <param name="learned">Accumulated learned technique ids.</param>
    private static void ApplyForm(PlayerSessionPartner partner, int form, int digimonExp, FighterData fighters, List<int> learned)
    {
        if (!fighters.Digimon.TryGetValue(form, out DigimonData? data))
        {
            return;
        }

        PartnerFormProgress progress = partner.FormProgress(form);
        progress.Exp = Math.Min(9_999_999, progress.Exp + Math.Max(0, digimonExp));
        RaiseLevel(progress, data.ExpLevel);
        LearnSkills(partner, progress, data, learned);
    }

    /// <summary>Raises a form at ten experience per level until its decoded experience threshold.</summary>
    /// <param name="progress">Mutable form entry.</param>
    /// <param name="expLevel">Decoded threshold that changes the source rate.</param>
    private static void RaiseLevel(PartnerFormProgress progress, int expLevel)
    {
        while (progress.Level < 99 && progress.Exp >= RequiredExp(progress.Level, expLevel))
        {
            progress.Level++;
        }
    }

    /// <summary>Returns the cumulative source experience required for the next form level.</summary>
    /// <param name="level">Current form level.</param>
    /// <param name="expLevel">Decoded rate threshold.</param>
    /// <returns>Required cumulative experience.</returns>
    private static int RequiredExp(int level, int expLevel) => level < expLevel
        ? level * 10
        : ((expLevel - 1) * 10) + ((level - expLevel + 1) * 50);

    /// <summary>Grants every source skill whose form level has been reached.</summary>
    /// <param name="partner">Persistent form owner.</param>
    /// <param name="progress">Advanced form entry.</param>
    /// <param name="data">Decoded skill thresholds.</param>
    /// <param name="learned">Accumulated learned ids.</param>
    private static void LearnSkills(PlayerSessionPartner partner, PartnerFormProgress progress, DigimonData data, List<int> learned)
    {
        for (int index = 1; index < data.Skills.Count && index - 1 < data.SkillLevels.Count; index++)
        {
            int technique = data.Skills[index];
            if (progress.Level >= data.SkillLevels[index - 1] && partner.LearnTechnique(progress.FormId, technique))
            {
                learned.Add(technique);
            }
        }
    }

    /// <summary>Repeatedly learns the first decoded evolution whose prerequisites are now met.</summary>
    /// <param name="partner">Persistent partner state.</param>
    /// <param name="evolutions">Ordered decoded evolution list.</param>
    /// <returns>New form ids in source-list order.</returns>
    private static int[] LearnForms(PlayerSessionPartner partner, PartnerDigivolution evolutions)
    {
        var result = new List<int>();
        DigivolutionEntry? next;
        while ((next = evolutions.Evolutions.FirstOrDefault(entry => Eligible(partner, entry))) is not null)
        {
            partner.AcquireForm(next.DigimonId);
            result.Add(next.DigimonId);
        }

        return [.. result];
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

    /// <summary>Checks the one optional source stat gate.</summary>
    /// <param name="growth">Current partner growth.</param>
    /// <param name="stat">Decoded source stat selector.</param>
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
