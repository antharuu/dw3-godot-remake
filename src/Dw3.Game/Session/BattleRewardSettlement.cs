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
            FightReportInput formReport = BattleFormSettlement.Apply(session, flow.State, victory, resolution.FightReport);
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
