using Dw3Production.Conversion;
using Dw3Production.Session;

namespace Dw3Production.Simulation;

/// <summary>
/// Applies the original report's per-slot form progression: each used equipped form receives its share of the
/// form experience, and only a form that gained a level then acquires its reached skills and masters the
/// reached ones. The partner's evolution list is evaluated afterwards.
/// </summary>
public static class PartnerFormProgression
{
    /// <summary>
    /// Fills every empty skill entry whose decoded technique exists and whose acquisition level is at or below the
    /// form's current level. Call once at session initialization to seed a form with its level-appropriate inherent
    /// skills without requiring a prior level-up.
    /// </summary>
    /// <param name="progress">Form progress entry to populate.</param>
    /// <param name="data">Decoded form record providing skills and acquisition levels.</param>
    public static void InitializeInherentSkills(PartnerFormProgress progress, DigimonData data)
    {
        ArgumentNullException.ThrowIfNull(progress);
        ArgumentNullException.ThrowIfNull(data);
        AcquireSkills(progress, data);
    }

    /// <summary>Applies one battle's form progression to one partner that fought.</summary>
    /// <param name="partner">Persistent owner of form entries.</param>
    /// <param name="usedSlots">Equipped slot indices the partner fought as, in slot order.</param>
    /// <param name="award">Battle-wide share inputs.</param>
    /// <param name="fighters">Decoded form records.</param>
    /// <param name="evolutions">Decoded evolution list, or null when none is known.</param>
    /// <returns>Per-slot rows, unlocked forms and the acquired form count after the unlocks.</returns>
    /// <exception cref="InvalidOperationException">A used slot is empty, the base form, or not decoded.</exception>
    public static FormProgressionResult Apply(
        PlayerSessionPartner partner,
        IReadOnlyList<int> usedSlots,
        FormExpAward award,
        FighterData fighters,
        PartnerDigivolution? evolutions)
    {
        ArgumentNullException.ThrowIfNull(partner);
        ArgumentNullException.ThrowIfNull(usedSlots);
        ArgumentNullException.ThrowIfNull(award);
        ArgumentNullException.ThrowIfNull(fighters);
        FightReportFormSlot[] rows = [.. usedSlots.Order().Select(slot => ApplySlot(partner, slot, award, fighters))];
        IReadOnlyList<int> learned = evolutions is null ? [] : FormUnlockRules.LearnForms(partner, evolutions);
        return new FormProgressionResult(rows, learned, partner.AcquiredFormCount);
    }

    /// <summary>Grants one used slot's share and the skills its level-up brings.</summary>
    /// <param name="partner">Persistent form owner.</param>
    /// <param name="slot">Used equipped slot.</param>
    /// <param name="award">Battle-wide share inputs.</param>
    /// <param name="fighters">Decoded form records.</param>
    /// <returns>The slot's report row.</returns>
    private static FightReportFormSlot ApplySlot(PlayerSessionPartner partner, int slot, FormExpAward award, FighterData fighters)
    {
        int form = slot >= 0 && slot < partner.EquippedForms.Count ? partner.EquippedForms[slot] : 0;
        if (form <= 0 || form == partner.OriginFormId)
        {
            throw new InvalidOperationException($"used slot {slot} holds no equipped form");
        }

        if (!fighters.Digimon.TryGetValue(form, out DigimonData? data))
        {
            throw new InvalidOperationException($"used form {form} is not decoded");
        }

        PartnerFormProgress progress = partner.FormProgress(form);
        int expBefore = progress.Exp;
        int levelBefore = progress.Level;
        bool leveled = FormExpRules.AddExp(progress, FormExpRules.Share(award, levelBefore, data.ExpLevel), data.ExpLevel);
        IReadOnlyList<int> acquired = leveled ? AcquireSkills(progress, data) : [];
        IReadOnlyList<int> mastered = leveled ? MasterSkills(progress, data) : [];
        return new FightReportFormSlot(slot, form, expBefore, progress.Exp - expBefore, levelBefore, progress.Level, acquired, mastered);
    }

    /// <summary>Fills every empty skill entry whose decoded skill exists and whose acquisition level is reached.</summary>
    /// <param name="progress">Leveled form entry.</param>
    /// <param name="data">Decoded form record, whose skill zero is the basic attack.</param>
    /// <returns>Newly acquired technique ids in entry order.</returns>
    private static List<int> AcquireSkills(PartnerFormProgress progress, DigimonData data)
    {
        var acquired = new List<int>();
        for (int index = 0; index < PartnerFormProgress.SkillEntryCount; index++)
        {
            int technique = index + 1 < data.Skills.Count ? BattleSetupFactory.SkillId(data.Skills[index + 1]) : 0;
            if (index < data.SkillLevels.Count && progress.Level >= data.SkillLevels[index] && progress.AcquireSkill(index, technique))
            {
                acquired.Add(technique);
            }
        }

        return acquired;
    }

    /// <summary>Masters every acquired masterable entry whose decoded mastery level is reached.</summary>
    /// <param name="progress">Leveled form entry.</param>
    /// <param name="data">Decoded form record.</param>
    /// <returns>Newly mastered technique ids in entry order.</returns>
    private static List<int> MasterSkills(PartnerFormProgress progress, DigimonData data)
    {
        var mastered = new List<int>();
        for (int index = 0; index < PartnerFormProgress.MasterableEntryCount; index++)
        {
            if (index < data.KnownLevels.Count && progress.Level >= data.KnownLevels[index] && progress.MasterSkill(index))
            {
                mastered.Add(progress.SkillEntries[index]);
            }
        }

        return mastered;
    }
}
