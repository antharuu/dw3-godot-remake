using Dw3Production.Conversion;
using Dw3Production.Presentation;

namespace Dw3Production.Simulation;

/// <summary>Applies a victory reward and builds both battle-report representations.</summary>
public static class BattleOutcomeResolver
{
    /// <summary>Shares experience among fighters that participated and snapshots the awards without applying them.</summary>
    /// <remarks>The original report credits each partner's experience during its raise and the money and item at their
    /// own pages, so growth is applied later through <see cref="ApplyPartnerGrowth"/> by the report settlement.</remarks>
    /// <param name="request">Terminal battle, reward and party growth state, which is only read.</param>
    /// <returns>Battle HUD and detailed report inputs.</returns>
    public static BattleOutcomeResolution ResolveVictory(BattleOutcomeRequest request)
    {
        IReadOnlyList<int> eligible = EligiblePartners(request);
        int expShare = GrowthCalculator.ExpShare(request.Reward.Exp, eligible.Count);
        int money = ResolveMoney(request.Reward, request.HasMoneyBonusEquipment);
        int item = ResolveItem(request);
        var partners = new List<BattlePartnerReportView>();
        var reportPartners = new List<FightReportPartner>();
        for (int index = 0; index < request.Flow.State.Players.Length; index++)
        {
            AddPartner(request, index, expShare, eligible.Contains(index), partners, reportPartners);
        }

        var report = new BattleReportView(
            BattleOutcome.Won,
            expShare,
            money,
            request.Reward.DigimonExp,
            partners,
            request.Text);
        var fightReport = new FightReportInput(reportPartners, money, item);
        string log = BuildLog(request.Encounter, request.Reward, eligible.Count, expShare, money, item, partners);
        return new BattleOutcomeResolution(report, fightReport, log);
    }

    /// <summary>Applies one partner's gained experience to its persistent growth, as the original report's raise does.</summary>
    /// <param name="tables">Decoded growth tables, or null when they were not loaded.</param>
    /// <param name="state">Mutable growth state, or null when the partner does not grow.</param>
    /// <param name="digimon">Base form record whose growth curve the partner levels with.</param>
    /// <param name="gained">Experience credited to the partner.</param>
    /// <param name="random">Shared stream consumed by every level-up's random gains.</param>
    /// <returns>True when the gain caused at least one level-up.</returns>
    public static bool ApplyPartnerGrowth(GrowthTables? tables, PartnerGrowthState? state, DigimonData digimon, int gained, IBattleRng random)
    {
        ArgumentNullException.ThrowIfNull(digimon);
        ArgumentNullException.ThrowIfNull(random);
        if (gained <= 0 || state is null || tables is null)
        {
            return false;
        }

        var curve = new PartnerGrowthCurve(
            digimon.ExpRate,
            digimon.HpGrowth,
            digimon.MpGrowth,
            digimon.StatGrowth,
            digimon.ResistGrowth);
        return GrowthCalculator.AddExp(tables, state, curve, gained, random);
    }

    /// <summary>Tests whether one party slot fought and is still alive at battle end, the original reward condition.</summary>
    /// <param name="state">Terminal battle state.</param>
    /// <param name="index">Party slot index.</param>
    /// <returns>True when the slot receives experience.</returns>
    internal static bool IsRewardEligible(BattleState state, int index) =>
        index >= 0 && index < state.Players.Length && index < state.Fought.Length
        && state.Fought[index] && state.Players[index] is { IsKnockedOut: false };

    /// <summary>Applies one occupied party slot and appends its two report records.</summary>
    /// <param name="request">Victory state.</param>
    /// <param name="index">Party slot index.</param>
    /// <param name="expShare">Experience allocated to each participating slot.</param>
    /// <param name="eligible">Whether the slot fought and remained alive at battle end.</param>
    /// <param name="partners">Battle HUD records under construction.</param>
    /// <param name="reportPartners">Detailed report records under construction.</param>
    private static void AddPartner(
        BattleOutcomeRequest request,
        int index,
        int expShare,
        bool eligible,
        List<BattlePartnerReportView> partners,
        List<FightReportPartner> reportPartners)
    {
        BattleFighter? fighter = request.Flow.State.Players[index];
        if (fighter is null || fighter.Id == 0)
        {
            return;
        }

        int gained = ResolveExperience(expShare, eligible, request.HasExperienceBonusEquipment(index));
        PartnerGrowthState? before = GrowthAt(request, index);
        int levelBefore = before?.Level ?? TestTeamGrowth.Level;
        int expBefore = before?.Exp ?? 0;
        partners.Add(new BattlePartnerReportView(
            fighter.Id,
            request.NameFor(fighter.Id),
            gained,
            levelBefore,
            false));
        reportPartners.Add(new FightReportPartner(index, index, fighter.Id, levelBefore, expBefore, gained, levelBefore, []));
    }

    /// <summary>Returns mutable growth state for a valid party slot.</summary>
    /// <param name="request">Victory state.</param>
    /// <param name="index">Party slot index.</param>
    /// <returns>The slot state, or null when growth was not initialized.</returns>
    private static PartnerGrowthState? GrowthAt(BattleOutcomeRequest request, int index) =>
        request.PartyGrowth is not null && index < request.PartyGrowth.Count
            ? request.PartyGrowth[index]
            : null;

    /// <summary>Applies the source one-fifth equipment bonus after sharing the encounter experience.</summary>
    /// <param name="expShare">Experience assigned to one eligible participant before equipment.</param>
    /// <param name="eligible">Whether the partner participated and remained eligible at battle end.</param>
    /// <param name="hasExperienceBonusEquipment">Whether this partner wears item 0x141.</param>
    /// <returns>The exact experience amount credited to the partner.</returns>
    private static int ResolveExperience(int expShare, bool eligible, bool hasExperienceBonusEquipment)
    {
        if (!eligible)
        {
            return 0;
        }

        return hasExperienceBonusEquipment ? expShare + (expShare / 5) : expShare;
    }

    /// <summary>Lists the party slots that fought and are still alive at battle end.</summary>
    /// <param name="request">Terminal victory state.</param>
    /// <returns>Source-eligible party indices in their fixed party order.</returns>
    private static IReadOnlyList<int> EligiblePartners(BattleOutcomeRequest request) =>
        [.. Enumerable.Range(0, request.Flow.State.Players.Length).Where(index => IsRewardEligible(request.Flow.State, index))];

    /// <summary>Applies the source fifth bonus for the equipped money item.</summary>
    /// <param name="reward">Encounter reward data.</param>
    /// <param name="hasMoneyBonusEquipment">Whether the active partner has the qualifying equipment.</param>
    /// <returns>The report's effective money award.</returns>
    private static int ResolveMoney(BattleReward reward, bool hasMoneyBonusEquipment) =>
        hasMoneyBonusEquipment ? reward.Money + (reward.Money / 5) : reward.Money;

    /// <summary>Selects the source's one candidate enemy item or overrides it with a scripted prize.</summary>
    /// <param name="request">Terminal victory state and shared random source.</param>
    /// <returns>The item id to report, or zero when no drop succeeds.</returns>
    private static int ResolveItem(BattleOutcomeRequest request)
    {
        BattleFighter[] candidates = [.. request.Flow.State.Enemies
            .OfType<BattleFighter>()
            .Where(enemy => enemy.Id != 0 && enemy.Item > 0)];
        if (candidates.Length == 0)
        {
            return request.HasPrize ? request.Prize : 0;
        }

        BattleFighter candidate = candidates[request.Random.NextValue() % candidates.Length];
        int item = candidate.ItemChance + 1 > request.Random.NextValue() % 1024 ? candidate.Item : 0;
        return request.HasPrize ? request.Prize : item;
    }

    /// <summary>Builds the existing structured reward diagnostic.</summary>
    /// <param name="encounter">Encounter id.</param>
    /// <param name="reward">Encounter reward.</param>
    /// <param name="fought">Number of participating partners.</param>
    /// <param name="expShare">Experience per participating partner.</param>
    /// <param name="money">Effective money award after source equipment bonuses.</param>
    /// <param name="item">Selected or scripted item award.</param>
    /// <param name="partners">Resolved report records.</param>
    /// <returns>The diagnostic message.</returns>
    private static string BuildLog(
        int encounter,
        BattleReward reward,
        int fought,
        int expShare,
        int money,
        int item,
        IReadOnlyList<BattlePartnerReportView> partners) =>
        $"battle-reward encounter={encounter} exp={reward.Exp} money={money} digimonExp={reward.DigimonExp} item={item} fought={fought} share={expShare} applied=[{string.Join(",", partners.Select(partner => $"fighter={partner.Id}:+{partner.ExpGained}"))}]";
}
