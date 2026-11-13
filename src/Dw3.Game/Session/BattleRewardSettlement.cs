using Dw3Production.Conversion;
using Dw3Production.Simulation;

namespace Dw3Production.Session;

/// <summary>Resolves a won battle's reward and delivers it to the persistent session.</summary>
public static class BattleRewardSettlement
{
    /// <summary>Resolves the victory reward and credits money, item, partner growth and used-form progress to the session.</summary>
    /// <param name="session">Persistent player state receiving the reward, or null when none is loaded.</param>
    /// <param name="flow">Terminal battle flow, which must have been won.</param>
    /// <param name="victory">Encounter reward and decoded data.</param>
    /// <returns>The resolved report inputs, whose money, item and form deltas were delivered exactly once.</returns>
    /// <exception cref="InvalidOperationException">The battle was not won.</exception>
    public static BattleOutcomeResolution SettleVictory(PlayerSession? session, BattleFlowController flow, BattleVictoryContext victory)
    {
        ArgumentNullException.ThrowIfNull(flow);
        ArgumentNullException.ThrowIfNull(victory);
        if (flow.Outcome != BattleOutcome.Won)
        {
            throw new InvalidOperationException($"reward settlement requested for outcome {flow.Outcome}");
        }

        PartnerGrowthState?[] before = [.. session?.PartyGrowth.Select(growth => growth?.Copy()) ?? []];
        BattleOutcomeResolution resolution = BattleOutcomeResolver.ResolveVictory(BuildRequest(session, flow, victory));
        if (session is not null)
        {
            Deliver(session, resolution.FightReport, before);
            FightReportInput formReport = ApplyFormProgression(session, flow, victory, resolution.FightReport);
            resolution = resolution with { FightReport = formReport };
        }

        return resolution;
    }

    /// <summary>Builds the resolver request from the session's growth and equipment.</summary>
    /// <param name="session">Persistent player state, or null.</param>
    /// <param name="flow">Won battle flow.</param>
    /// <param name="victory">Encounter reward and decoded data.</param>
    /// <returns>The request the reward resolver reads.</returns>
    private static BattleOutcomeRequest BuildRequest(PlayerSession? session, BattleFlowController flow, BattleVictoryContext victory) => new()
    {
        Flow = flow,
        Reward = victory.Reward,
        Encounter = victory.Encounter,
        PartyGrowth = session?.PartyGrowth,
        GrowthTables = victory.GrowthTables,
        Random = victory.Random,
        DigimonFor = index => BaseDigimon(session, victory.Fighters, index),
        NameFor = victory.NameFor,
        HasMoneyBonusEquipment = BattleRewardEquipment.HasMoneyBonus(session, flow.State.ActivePlayer),
        HasExperienceBonusEquipment = index => BattleRewardEquipment.HasExperienceBonus(session, index),
        HasPrize = victory.Prize.HasValue,
        Prize = victory.Prize ?? 0,
        Text = victory.Text,
    };

    /// <summary>Credits the resolved money and item and carries each partner's growth into its battle seed.</summary>
    /// <param name="session">Persistent player state.</param>
    /// <param name="report">Resolved report amounts.</param>
    /// <param name="before">Growth captured before the resolver applied experience.</param>
    private static void Deliver(PlayerSession session, FightReportInput report, PartnerGrowthState?[] before)
    {
        session.CreditMoney(report.Money);
        session.CreditItem(report.Item);
        for (int index = 0; index < session.Partners.Length && index < before.Length; index++)
        {
            if (before[index] is PartnerGrowthState snapshot)
            {
                session.Partners[index]?.ApplyGrowthSince(snapshot);
            }
        }
    }

    /// <summary>Credits every permanent form that acted and maps the exact deltas into report panels.</summary>
    /// <param name="session">Persistent partners that own the form entries.</param>
    /// <param name="flow">Completed battle retaining used forms by party slot.</param>
    /// <param name="victory">Decoded form and evolution data.</param>
    /// <param name="report">Original partner-level report data.</param>
    /// <returns>Report data augmented with form levels, skills and newly unlocked forms.</returns>
    private static FightReportInput ApplyFormProgression(PlayerSession session, BattleFlowController flow, BattleVictoryContext victory, FightReportInput report)
    {
        if (victory.Digivolutions is null)
        {
            return report;
        }

        FightReportPartner[] partners = [.. report.Partners.Select(partner => ApplyPartnerFormProgression(session, flow, victory, partner))];
        return report with { Partners = partners };
    }

    /// <summary>Applies one report partner's used-form experience and snapshots it for report playback.</summary>
    /// <param name="session">Persistent session holding the partner.</param>
    /// <param name="flow">Completed battle with use markers.</param>
    /// <param name="victory">Decoded form sources and reward.</param>
    /// <param name="report">Existing partner-level report state.</param>
    /// <returns>The report state enriched with form-specific results.</returns>
    private static FightReportPartner ApplyPartnerFormProgression(PlayerSession session, BattleFlowController flow, BattleVictoryContext victory, FightReportPartner report)
    {
        PlayerSessionPartner? partner = PartnerAt(session, report.PartyIndex);
        PartnerDigivolution? tree = EvolutionFor(victory.Digivolutions!, partner);
        if (partner is null || tree is null)
        {
            return report;
        }

        IReadOnlyCollection<int> usedForms = UsedFormsAt(flow.State, report.PartyIndex);
        FormSnapshot[] before = [.. usedForms.Where(partner.KnownForms.Contains).Select(form => FormSnapshot.Capture(partner.FormProgress(form)))];
        FormProgressionResult result = PartnerFormProgression.Apply(partner, usedForms, victory.Reward.DigimonExp, victory.Fighters, tree);
        FightReportFormSlot[] slots = [.. before.Select(snapshot => snapshot.ToReport(partner.FormProgress(snapshot.FormId)))];
        FightReportLearnedForm[] learnedForms = [.. result.LearnedForms.Select(form => ToReportLearnedForm(victory.Fighters, form))];
        return report with { SlotDigimonIds = [partner.OriginFormId, .. partner.EquippedForms], FormSlots = slots, LearnedForms = learnedForms };
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

    /// <summary>Finds the decoded evolution tree belonging to a persistent partner.</summary>
    /// <param name="data">All decoded partner trees.</param>
    /// <param name="partner">Persistent partner, if any.</param>
    /// <returns>The matching base-form tree, or null when unavailable.</returns>
    private static PartnerDigivolution? EvolutionFor(DigivolutionData data, PlayerSessionPartner? partner) =>
        partner is null ? null : data.Partners.FirstOrDefault(candidate => candidate.BaseId == partner.OriginFormId);

    /// <summary>Returns form ids that were actually used by one party slot.</summary>
    /// <param name="state">Completed battle state.</param>
    /// <param name="index">Party slot.</param>
    /// <returns>The retained used-form set, or an empty set for an invalid slot.</returns>
    private static IReadOnlyCollection<int> UsedFormsAt(BattleState state, int index) =>
        index >= 0 && index < state.UsedFormsBySlot.Length ? state.UsedFormsBySlot[index] : [];

    /// <summary>Returns the base Digimon record whose growth curve the partner levels with.</summary>
    /// <param name="session">Persistent player state.</param>
    /// <param name="fighters">Decoded fighter table.</param>
    /// <param name="index">Party slot.</param>
    /// <returns>The partner's base form record.</returns>
    /// <exception cref="InvalidOperationException">The slot has no partner or its base form is not decoded.</exception>
    private static DigimonData BaseDigimon(PlayerSession? session, FighterData fighters, int index)
    {
        PlayerSessionPartner partner = session is not null && index >= 0 && index < session.Partners.Length
            ? session.Partners[index] ?? throw new InvalidOperationException($"party slot {index} has no partner")
            : throw new InvalidOperationException($"party slot {index} has no session partner");
        return fighters.Digimon.TryGetValue(partner.OriginFormId, out DigimonData? digimon)
            ? digimon
            : throw new InvalidOperationException($"party slot {index} base form {partner.OriginFormId} is not decoded");
    }
}
