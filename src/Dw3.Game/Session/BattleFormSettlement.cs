using Dw3Production.Conversion;
using Dw3Production.Simulation;

namespace Dw3Production.Session;

/// <summary>
/// Settles a won battle's equipped-form progression for every partner that fought and maps it into the report.
/// The share uses the partner level before its own experience, and the used equipped slots counted over every
/// partner that fought, so two partners fighting as the same form id count as two used slots.
/// </summary>
internal static class BattleFormSettlement
{
    /// <summary>Applies every eligible partner's used-slot progression and returns the enriched report.</summary>
    /// <param name="session">Persistent partners that own the form entries.</param>
    /// <param name="state">Completed battle state retaining used forms by party slot.</param>
    /// <param name="victory">Encounter reward and decoded form and evolution data.</param>
    /// <param name="report">Report whose partner levels are still the levels before this battle's experience.</param>
    /// <returns>The report with equipped slots, form rows, unlocked forms and acquired-form counts.</returns>
    public static FightReportInput Apply(PlayerSession session, BattleState state, BattleVictoryContext victory, FightReportInput report)
    {
        IReadOnlyList<int>[] used = [.. report.Partners.Select(partner => UsedSlots(PartnerAt(session, partner.PartyIndex), state, partner.PartyIndex))];
        int globalUsed = used.Sum(slots => slots.Count);
        FightReportPartner[] partners = [.. report.Partners.Select((partner, index) => ApplyPartner(session, state, victory, partner, used[index], globalUsed))];
        return report with { Partners = partners };
    }

    /// <summary>Lists the equipped slot indices a reward-eligible partner fought as.</summary>
    /// <param name="partner">Persistent partner, or null for an empty slot.</param>
    /// <param name="state">Completed battle state.</param>
    /// <param name="index">Party slot.</param>
    /// <returns>Used equipped slot indices in slot order, empty when the partner cannot receive rewards.</returns>
    private static IReadOnlyList<int> UsedSlots(PlayerSessionPartner? partner, BattleState state, int index)
    {
        if (partner is null || !BattleOutcomeResolver.IsRewardEligible(state, index))
        {
            return [];
        }

        IReadOnlySet<int> forms = state.UsedFormsBySlot[index];
        return [.. Enumerable.Range(0, partner.EquippedForms.Count)
            .Where(slot => partner.EquippedForms[slot] != partner.OriginFormId && forms.Contains(partner.EquippedForms[slot]))];
    }

    /// <summary>Applies one report partner's progression and snapshots it for report playback.</summary>
    /// <param name="session">Persistent session holding the partner.</param>
    /// <param name="state">Completed battle state.</param>
    /// <param name="victory">Encounter reward and decoded data.</param>
    /// <param name="report">Partner report row.</param>
    /// <param name="used">The partner's used equipped slots.</param>
    /// <param name="globalUsed">Used slots over every partner that fought.</param>
    /// <returns>The enriched report row.</returns>
    private static FightReportPartner ApplyPartner(PlayerSession session, BattleState state, BattleVictoryContext victory, FightReportPartner report, IReadOnlyList<int> used, int globalUsed)
    {
        PlayerSessionPartner? partner = PartnerAt(session, report.PartyIndex);
        if (partner is null)
        {
            return report;
        }

        IReadOnlyList<int> equipped = [.. partner.EquippedForms];
        if (!BattleOutcomeResolver.IsRewardEligible(state, report.PartyIndex))
        {
            return report with { SlotDigimonIds = equipped };
        }

        var award = new FormExpAward(victory.Reward.DigimonExp, report.LevelBefore, globalUsed);
        FormProgressionResult result = PartnerFormProgression.Apply(partner, used, award, victory.Fighters, EvolutionFor(victory.Digivolutions, partner));
        return report with
        {
            SlotDigimonIds = equipped,
            FormSlots = result.Slots,
            LearnedForms = [.. result.LearnedForms.Select(form => ToReportLearnedForm(victory.Fighters, form))],
            AcquiredFormCount = result.AcquiredFormCount,
        };
    }

    /// <summary>Maps an unlocked form to the report's persistent and display identities.</summary>
    /// <param name="fighters">Decoded fighter table containing the form name entry.</param>
    /// <param name="form">Newly acquired persistent form identifier.</param>
    /// <returns>The report row, preserving the form id when the decoded display record is absent.</returns>
    private static FightReportLearnedForm ToReportLearnedForm(FighterData fighters, int form) =>
        fighters.Digimon.TryGetValue(form, out DigimonData? data)
            ? new FightReportLearnedForm(form, data.NameId)
            : new FightReportLearnedForm(form, form);

    /// <summary>Returns the partner at one valid party index.</summary>
    /// <param name="session">Persistent party.</param>
    /// <param name="index">Requested slot.</param>
    /// <returns>The partner, or null for an invalid or empty slot.</returns>
    private static PlayerSessionPartner? PartnerAt(PlayerSession session, int index) => index >= 0 && index < session.Partners.Length ? session.Partners[index] : null;

    /// <summary>Finds the decoded evolution list belonging to a persistent partner.</summary>
    /// <param name="data">All decoded partner lists, or null when none were loaded.</param>
    /// <param name="partner">Persistent partner.</param>
    /// <returns>The matching base-form list, or null when unavailable.</returns>
    private static PartnerDigivolution? EvolutionFor(DigivolutionData? data, PlayerSessionPartner partner) =>
        data?.Partners.FirstOrDefault(candidate => candidate.BaseId == partner.OriginFormId);
}
