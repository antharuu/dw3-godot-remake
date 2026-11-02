using Dw3Production.Conversion;
using Dw3Production.Presentation;

namespace Dw3Production.Simulation;

/// <summary>Applies a victory reward and builds both battle-report representations.</summary>
public static class BattleOutcomeResolver
{
    /// <summary>Shares experience among fighters that participated and applies growth.</summary>
    /// <param name="request">Terminal battle, reward and party growth state.</param>
    /// <returns>Battle HUD and detailed report inputs.</returns>
    public static BattleOutcomeResolution ResolveVictory(BattleOutcomeRequest request)
    {
        int fought = request.Flow.State.Fought.Count(foughtFlag => foughtFlag);
        int expShare = GrowthCalculator.ExpShare(request.Reward.Exp, fought);
        var partners = new List<BattlePartnerReportView>();
        var reportPartners = new List<FightReportPartner>();
        for (int index = 0; index < request.Flow.State.Players.Length; index++)
        {
            AddPartner(request, index, expShare, partners, reportPartners);
        }

        var report = new BattleReportView(
            BattleOutcome.Won,
            expShare,
            request.Reward.Money,
            request.Reward.DigimonExp,
            partners,
            request.Text);
        var fightReport = new FightReportInput(reportPartners, request.Reward.Money, Item: 0);
        string log = BuildLog(request.Encounter, request.Reward, fought, expShare, partners);
        return new BattleOutcomeResolution(report, fightReport, log);
    }

    /// <summary>Applies one occupied party slot and appends its two report records.</summary>
    /// <param name="request">Victory state.</param>
    /// <param name="index">Party slot index.</param>
    /// <param name="expShare">Experience allocated to each participating slot.</param>
    /// <param name="partners">Battle HUD records under construction.</param>
    /// <param name="reportPartners">Detailed report records under construction.</param>
    private static void AddPartner(
        BattleOutcomeRequest request,
        int index,
        int expShare,
        List<BattlePartnerReportView> partners,
        List<FightReportPartner> reportPartners)
    {
        BattleFighter? fighter = request.Flow.State.Players[index];
        if (fighter is null || fighter.Id == 0)
        {
            return;
        }

        int gained = request.Flow.State.Fought[index] ? expShare : 0;
        PartnerGrowthState? before = GrowthAt(request, index);
        int levelBefore = before?.Level ?? TestTeamGrowth.Level;
        int expBefore = before?.Exp ?? 0;
        bool leveled = ApplyGrowth(request, index, gained);
        int level = GrowthAt(request, index)?.Level ?? TestTeamGrowth.Level;
        partners.Add(new BattlePartnerReportView(
            fighter.Id,
            request.NameFor(fighter.Id),
            gained,
            level,
            leveled));
        reportPartners.Add(new FightReportPartner(index, index, fighter.Id, levelBefore, expBefore, gained, level, []));
    }

    /// <summary>Returns mutable growth state for a valid party slot.</summary>
    /// <param name="request">Victory state.</param>
    /// <param name="index">Party slot index.</param>
    /// <returns>The slot state, or null when growth was not initialized.</returns>
    private static PartnerGrowthState? GrowthAt(BattleOutcomeRequest request, int index) =>
        request.PartyGrowth is not null && index < request.PartyGrowth.Count
            ? request.PartyGrowth[index]
            : null;

    /// <summary>Applies gained experience when every decoded growth dependency is available.</summary>
    /// <param name="request">Victory state.</param>
    /// <param name="index">Party slot index.</param>
    /// <param name="gained">Experience gained by the slot.</param>
    /// <returns>True when the gain caused at least one level-up.</returns>
    private static bool ApplyGrowth(BattleOutcomeRequest request, int index, int gained)
    {
        PartnerGrowthState? state = GrowthAt(request, index);
        if (gained <= 0 || state is null || request.GrowthTables is null)
        {
            return false;
        }

        DigimonData digimon = request.DigimonFor(index);
        var curve = new PartnerGrowthCurve(
            digimon.ExpRate,
            digimon.HpGrowth,
            digimon.MpGrowth,
            digimon.StatGrowth,
            digimon.ResistGrowth);
        return GrowthCalculator.AddExp(request.GrowthTables, state, curve, gained, request.Random);
    }

    /// <summary>Builds the existing structured reward diagnostic.</summary>
    /// <param name="encounter">Encounter id.</param>
    /// <param name="reward">Encounter reward.</param>
    /// <param name="fought">Number of participating partners.</param>
    /// <param name="expShare">Experience per participating partner.</param>
    /// <param name="partners">Resolved report records.</param>
    /// <returns>The diagnostic message.</returns>
    private static string BuildLog(
        int encounter,
        BattleReward reward,
        int fought,
        int expShare,
        IReadOnlyList<BattlePartnerReportView> partners) =>
        $"battle-reward encounter={encounter} exp={reward.Exp} money={reward.Money} digimonExp={reward.DigimonExp} fought={fought} share={expShare} applied=[{string.Join(",", partners.Select(partner => $"fighter={partner.Id}:+{partner.ExpGained}"))}]";
}
