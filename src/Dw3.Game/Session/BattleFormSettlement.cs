using Dw3Production.Conversion;
using Dw3Production.Simulation;

namespace Dw3Production.Session;

/// <summary>
/// Settles a won battle's equipped-form progression for one raised partner and maps it into the report.
/// The share uses the partner level before its own experience, and the used equipped slots counted over every
/// partner that fought, so two partners fighting as the same form id count as two used slots.
/// </summary>
internal static class BattleFormSettlement
{
    /// <summary>Lists each report partner's used equipped slots at the battle's end, before any reward is applied.</summary>
    /// <param name="session">Persistent partners that own the form entries.</param>
    /// <param name="state">Completed battle state retaining used forms by party slot.</param>
    /// <param name="report">Report partners in party order.</param>
    /// <returns>Used slot indices per report partner, empty for a partner that cannot receive rewards.</returns>
    public static IReadOnlyList<int>[] UsedSlots(PlayerSession session, BattleState state, FightReportInput report) =>
        [.. report.Partners.Select(partner => UsedSlots(PartnerAt(session, partner.PartyIndex), state, partner.PartyIndex))];

    /// <summary>Snapshots the equipped slots and their current levels shown before the partner's raise.</summary>
    /// <param name="session">Persistent party.</param>
    /// <param name="report">Report partner row.</param>
    /// <param name="partnerNumber">Partner number among the eight partners, selecting the report animation and name.</param>
    /// <returns>The row carrying the pre-raise slot display.</returns>
    public static FightReportPartner Snapshot(PlayerSession session, FightReportPartner report, int partnerNumber)
    {
        PlayerSessionPartner? partner = PartnerAt(session, report.PartyIndex);
        if (partner is null)
        {
            return report;
        }

        IReadOnlyList<int> equipped = [.. partner.EquippedForms];
        return report with
        {
            PartnerId = partnerNumber,
            SlotDigimonIds = equipped,
            SlotLevels = [.. equipped.Select(form => partner.FormProgress(form).Level)],
        };
    }

    /// <summary>Applies one raised partner's used-slot progression and unlocks, then enriches its report row.</summary>
    /// <param name="partner">Persistent partner being raised.</param>
    /// <param name="victory">Encounter reward and decoded form and evolution data.</param>
    /// <param name="report">Report row whose level is still the level before this battle's experience.</param>
    /// <param name="used">The partner's used equipped slots.</param>
    /// <param name="globalUsed">Used slots over every partner that fought.</param>
    /// <returns>The row with form rows, unlocked forms and acquired-form count.</returns>
    public static FightReportPartner ApplyPartner(PlayerSessionPartner partner, BattleVictoryContext victory, FightReportPartner report, IReadOnlyList<int> used, int globalUsed)
    {
        ArgumentNullException.ThrowIfNull(partner);
        ArgumentNullException.ThrowIfNull(victory);
        ArgumentNullException.ThrowIfNull(report);
        var award = new FormExpAward(victory.Reward.DigimonExp, report.LevelBefore, globalUsed);
        FormProgressionResult result = PartnerFormProgression.Apply(partner, used, award, victory.Fighters, EvolutionFor(victory.Digivolutions, partner));
        return report with
        {
            FormSlots = result.Slots,
            LearnedForms = [.. result.LearnedForms.Select(form => ToReportLearnedForm(partner, victory.Fighters, form))],
            AcquiredFormCount = result.AcquiredFormCount,
        };
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

    /// <summary>Maps an unlocked form to the report's persistent and display identities and its new level.</summary>
    /// <param name="partner">Partner that acquired the form.</param>
    /// <param name="fighters">Decoded fighter table containing the form name entry.</param>
    /// <param name="form">Newly acquired persistent form identifier.</param>
    /// <returns>The report row, preserving the form id when the decoded display record is absent.</returns>
    private static FightReportLearnedForm ToReportLearnedForm(PlayerSessionPartner partner, FighterData fighters, int form) =>
        new(form, fighters.Digimon.TryGetValue(form, out DigimonData? data) ? data.NameId : form)
        {
            Level = partner.FormProgress(form).Level,
        };

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
