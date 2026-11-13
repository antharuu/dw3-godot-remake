using Dw3Production.Conversion;
using Dw3Production.Simulation;

namespace Dw3Production.Session;

/// <summary>Resolves a won battle's reward at the battle's end and hands its delivery to a staged settlement.</summary>
public static class BattleRewardSettlement
{
    /// <summary>
    /// Resolves the victory reward and snapshots it without crediting anything. The returned settlement credits each
    /// partner's experience and forms, the money and the item at the report stages where the original does.
    /// </summary>
    /// <param name="session">Persistent player state that will receive the reward, or null when none is loaded.</param>
    /// <param name="flow">Terminal battle flow, which must have been won.</param>
    /// <param name="victory">Encounter reward and decoded data.</param>
    /// <returns>The resolved report inputs and, with a session, the settlement that delivers them exactly once.</returns>
    /// <exception cref="InvalidOperationException">The battle was not won.</exception>
    public static BattleOutcomeResolution SettleVictory(PlayerSession? session, BattleFlowController flow, BattleVictoryContext victory)
    {
        ArgumentNullException.ThrowIfNull(flow);
        ArgumentNullException.ThrowIfNull(victory);
        if (flow.Outcome != BattleOutcome.Won)
        {
            throw new InvalidOperationException($"reward settlement requested for outcome {flow.Outcome}");
        }

        BattleOutcomeResolution resolution = BattleOutcomeResolver.ResolveVictory(BuildRequest(session, flow, victory));
        if (session is null)
        {
            return resolution;
        }

        FightReportInput snapshot = resolution.FightReport with
        {
            Partners = [.. resolution.FightReport.Partners.Select(partner => BattleFormSettlement.Snapshot(session, partner, PartnerNumber(session, victory.Fighters, partner)))],
        };
        var settlement = new VictorySettlement(session, victory, snapshot, BattleFormSettlement.UsedSlots(session, flow.State, snapshot));
        return resolution with { FightReport = snapshot, Settlement = settlement };
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
        Random = victory.Random,
        NameFor = victory.NameFor,
        HasMoneyBonusEquipment = BattleRewardEquipment.HasMoneyBonus(session, flow.State.ActivePlayer),
        HasExperienceBonusEquipment = index => BattleRewardEquipment.HasExperienceBonus(session, index),
        HasPrize = victory.Prize.HasValue,
        Prize = victory.Prize ?? 0,
        Text = victory.Text,
    };

    /// <summary>Returns the partner number, among the eight partners, of the partner in one report row.</summary>
    /// <param name="session">Persistent party.</param>
    /// <param name="fighters">Decoded fighter table whose first eight entries are the partners in order.</param>
    /// <param name="partner">Report row.</param>
    /// <returns>The base form's table position, or the row's party index when the partner or its base is unknown.</returns>
    private static int PartnerNumber(PlayerSession session, FighterData fighters, FightReportPartner partner)
    {
        PlayerSessionPartner? owner = partner.PartyIndex >= 0 && partner.PartyIndex < session.Partners.Length ? session.Partners[partner.PartyIndex] : null;
        int number = owner is null ? -1 : BattleSetupFactory.DigimonIndexFor(fighters, owner.OriginFormId);
        return number >= 0 ? number : partner.PartnerId;
    }
}
