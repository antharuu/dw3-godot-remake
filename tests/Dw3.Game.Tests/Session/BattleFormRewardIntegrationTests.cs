using Dw3Production;
using Dw3Production.Conversion;
using Dw3Production.Session;
using Dw3Production.Simulation;
using Xunit;

namespace Dw3.Game.Tests.Session;

/// <summary>Verifies that a won session battle delivers used-form experience into the report and the next battle catalogue.</summary>
public sealed class BattleFormRewardIntegrationTests
{
    /// <summary>Credits only the used form, reports its level and learned techniques, unlocks the next form, and exposes it to the next battle.</summary>
    [Fact]
    public void WonBattle_DeliversUsedFormProgressionToReportAndNextBattle()
    {
        PlayerSession session = RewardSessionFixture.CreateSession(new Dictionary<int, int>());
        FighterData fighters = Fighters();
        BattleFlowController first = Build(session, fighters);
        RewardSessionFixture.Win(first);

        BattleOutcomeResolution resolution = BattleRewardSettlement.SettleVictory(session, first, Context(fighters));
        BattleFlowController second = Build(session, fighters);

        PlayerSessionPartner partner = Assert.IsType<PlayerSessionPartner>(session.Partners[0]);
        Assert.Equal([1], first.State.UsedFormsBySlot[0]);
        Assert.Equal(30, partner.FormProgress(1).Exp);
        Assert.Equal(4, partner.FormProgress(1).Level);
        Assert.Equal([1, 2, 11, 12], partner.FormProgress(1).Techniques);
        Assert.Contains(2, partner.KnownForms);
        Assert.Contains(2, partner.EquippedForms);
        FightReportFormSlot slot = Assert.Single(resolution.FightReport.Partners[0].FormSlots);
        Assert.Equal((1, 0, 30, 1, 4), (slot.DigimonId, slot.ExpBefore, slot.ExpGained, slot.LevelBefore, slot.LevelAfter));
        Assert.Equal([11, 12], slot.LearnedTechniques);
        Assert.Equal([2], resolution.FightReport.Partners[0].LearnedForms.Select(form => form.DigimonId));
        Assert.Contains(2, second.PlayerDigivolutions);
        Assert.Contains(second.PlayerTechniquesByForm[1], technique => technique.Id == 11);
    }

    /// <summary>Runs the real report controller over settled form deltas without leaving the slot sequence unwired.</summary>
    [Fact]
    public void WonBattle_ReportPlaysUsedFormLevelsSkillsAndUnlock()
    {
        PlayerSession session = RewardSessionFixture.CreateSession(new Dictionary<int, int>());
        FighterData fighters = Fighters();
        BattleFlowController flow = Build(session, fighters);
        RewardSessionFixture.Win(flow);
        BattleOutcomeResolution resolution = BattleRewardSettlement.SettleVictory(session, flow, Context(fighters));
        var report = new FightReportController(resolution.FightReport);
        var messages = new List<int>();
        var slotExpApplied = false;
        var slotBlinkFinished = false;

        for (int frame = 0; frame < 20_000 && !report.View.Done; frame++)
        {
            FightReportMessage? message = report.View.Message;
            if (message is not null && (messages.Count == 0 || messages[^1] != message.Entry))
            {
                messages.Add(message.Entry);
            }

            FightReportSlotView slot = report.View.Partners[0].Slots[0];
            slotExpApplied |= slot.ExpApplied;
            slotBlinkFinished |= slot.BlinkFrame == 10;
            report.Step(crossPressedEdge: true);
        }

        Assert.True(report.View.Done);
        Assert.DoesNotContain("report-digimon-exp-unwired", report.Logs);
        Assert.True(slotExpApplied);
        Assert.True(slotBlinkFinished);
        Assert.Equal([2, 4, 5, 6, 8, 10], messages);
    }

    /// <summary>Awards form experience that falls short of the next level and verifies that no level-up or skill messages appear.</summary>
    [Fact]
    public void WonBattle_NoLevelAward_ReportSkipsSlotLevelMessage()
    {
        PlayerSession session = RewardSessionFixture.CreateSession(new Dictionary<int, int>());
        FighterData fighters = Fighters();
        BattleFlowController flow = Build(session, fighters);
        RewardSessionFixture.Win(flow);

        // DigimonExp=5 gives form 1 five experience; it needs 10 to reach level 2.
        BattleVictoryContext ctx = new(
            new BattleReward(DigimonExp: 5, Exp: 0, Money: 0),
            1,
            fighters,
            RewardSessionFixture.GrowthTables(),
            new RewardSessionFixture.FixedRng(),
            id => BattleTextCatalog.Tokens([], $"fighter-{id}"),
            new BattleTextCatalog(RewardSessionFixture.Text()),
            Digivolutions: Digivolutions());
        BattleOutcomeResolution resolution = BattleRewardSettlement.SettleVictory(session, flow, ctx);

        PlayerSessionPartner partner = Assert.IsType<PlayerSessionPartner>(session.Partners[0]);
        Assert.Equal(5, partner.FormProgress(1).Exp);
        Assert.Equal(1, partner.FormProgress(1).Level);
        Assert.Empty(resolution.FightReport.Partners[0].LearnedForms);

        var report = new FightReportController(resolution.FightReport);
        var messages = new List<int>();
        var slotExpApplied = false;
        for (int frame = 0; frame < 20_000 && !report.View.Done; frame++)
        {
            FightReportMessage? message = report.View.Message;
            if (message is not null && (messages.Count == 0 || messages[^1] != message.Entry))
            {
                messages.Add(message.Entry);
            }

            slotExpApplied |= report.View.Partners[0].Slots[0].ExpApplied;
            report.Step(crossPressedEdge: true);
        }

        Assert.True(report.View.Done);
        Assert.True(slotExpApplied);
        Assert.Equal([2, 10], messages);
    }

    /// <summary>Verifies that a form auto-equipped to a free slot does not trigger the slots-full message.</summary>
    [Fact]
    public void WonBattle_AutoEquippedFormDoesNotTriggerSlotsFullMessage()
    {
        // Partner 0 starts with form 3 already equipped in slot 1; slot 2 is free.
        PlayerSessionPartner partner0 = new(
            RewardSessionFixture.Seed(1),
            knownForms: [1, 3],
            equippedForms: [3],
            knownTechniques: [1, 2],
            equipmentIds: [],
            growth: RewardSessionFixture.Growth());
        PlayerSession session = new(
            [partner0, RewardSessionFixture.Partner(2), RewardSessionFixture.Partner(3)],
            new Dictionary<int, int>());
        FighterData fighters = Fighters();
        BattleFlowController flow = Build(session, fighters);
        RewardSessionFixture.Win(flow);
        BattleOutcomeResolution resolution = BattleRewardSettlement.SettleVictory(session, flow, Context(fighters));

        // Form 2 is unlocked and auto-equipped to the free second slot.
        PlayerSessionPartner settled = Assert.IsType<PlayerSessionPartner>(session.Partners[0]);
        Assert.Contains(2, settled.EquippedForms);
        Assert.Equal([1, 3, 2], resolution.FightReport.Partners[0].SlotDigimonIds);

        var report = new FightReportController(resolution.FightReport);
        var messages = new List<int>();
        for (int frame = 0; frame < 20_000 && !report.View.Done; frame++)
        {
            FightReportMessage? message = report.View.Message;
            if (message is not null && (messages.Count == 0 || messages[^1] != message.Entry))
            {
                messages.Add(message.Entry);
            }

            report.Step(crossPressedEdge: true);
        }

        Assert.True(report.View.Done);
        Assert.DoesNotContain(9, messages);
    }

    /// <summary>Verifies that a form unlocked when all three slots are occupied triggers the slots-full message.</summary>
    [Fact]
    public void WonBattle_AllSlotsFull_ShowsSlotsFullMessage()
    {
        // Partner 0 starts with all three equipped slots taken; form 2 cannot be auto-equipped.
        PlayerSessionPartner partner0 = new(
            RewardSessionFixture.Seed(1),
            knownForms: [1, 3, 10, 20],
            equippedForms: [3, 10, 20],
            knownTechniques: [1, 2],
            equipmentIds: [],
            growth: RewardSessionFixture.Growth());
        PlayerSession session = new(
            [partner0, RewardSessionFixture.Partner(2), RewardSessionFixture.Partner(3)],
            new Dictionary<int, int>());
        FighterData fighters = Fighters();
        BattleFlowController flow = Build(session, fighters);
        RewardSessionFixture.Win(flow);
        BattleOutcomeResolution resolution = BattleRewardSettlement.SettleVictory(session, flow, Context(fighters));

        PlayerSessionPartner settled = Assert.IsType<PlayerSessionPartner>(session.Partners[0]);
        Assert.Contains(2, settled.KnownForms);
        Assert.DoesNotContain(2, settled.EquippedForms);

        var report = new FightReportController(resolution.FightReport);
        var messages = new List<int>();
        for (int frame = 0; frame < 20_000 && !report.View.Done; frame++)
        {
            FightReportMessage? message = report.View.Message;
            if (message is not null && (messages.Count == 0 || messages[^1] != message.Entry))
            {
                messages.Add(message.Entry);
            }

            report.Step(crossPressedEdge: true);
        }

        Assert.True(report.View.Done);
        Assert.Contains(9, messages);
    }

    /// <summary>Verifies that stepping a completed report is a no-op and that settlement runs only once.</summary>
    [Fact]
    public void WonBattle_RepeatedTicksAfterDoneDoNotApplyDoubleCredits()
    {
        PlayerSession session = RewardSessionFixture.CreateSession(new Dictionary<int, int>());
        FighterData fighters = Fighters();
        BattleFlowController flow = Build(session, fighters);
        RewardSessionFixture.Win(flow);
        BattleOutcomeResolution resolution = BattleRewardSettlement.SettleVictory(session, flow, Context(fighters));

        PlayerSessionPartner partner = Assert.IsType<PlayerSessionPartner>(session.Partners[0]);
        int expAfterSettlement = partner.FormProgress(1).Exp;

        var report = new FightReportController(resolution.FightReport);
        for (int frame = 0; frame < 20_000 && !report.View.Done; frame++)
        {
            report.Step(crossPressedEdge: true);
        }

        Assert.True(report.View.Done);
        for (int extra = 0; extra < 100; extra++)
        {
            report.Step(crossPressedEdge: true);
        }

        Assert.True(report.View.Done);
        Assert.Equal(expAfterSettlement, partner.FormProgress(1).Exp);
    }

    /// <summary>Builds a started session battle against the fixture encounter with the supplied form catalogue.</summary>
    /// <param name="session">Persistent session shared by both battles.</param>
    /// <param name="fighters">Form catalogue used by the battle and settlement.</param>
    /// <returns>A started production flow.</returns>
    private static BattleFlowController Build(PlayerSession session, FighterData fighters) =>
        GameHost.BattleSetupCoordinator.BuildForSession(
            new GameHost.BattleSetupCoordinator.BattleSessionBuildInput(
                fighters,
                Techniques(),
                RewardSessionFixture.Encounters(),
                1,
                session,
                RewardSessionFixture.Items(),
                RewardSessionFixture.Text(),
                new RewardSessionFixture.FixedRng()));

    /// <summary>Builds the victory inputs with a source-style tree that unlocks form two at base-form level two.</summary>
    /// <param name="fighters">The form catalogue used by the settlement.</param>
    /// <returns>The production settlement context.</returns>
    private static BattleVictoryContext Context(FighterData fighters) => new(
        new BattleReward(DigimonExp: 30, Exp: 0, Money: 0),
        1,
        fighters,
        RewardSessionFixture.GrowthTables(),
        new RewardSessionFixture.FixedRng(),
        id => BattleTextCatalog.Tokens([], $"fighter-{id}"),
        new BattleTextCatalog(RewardSessionFixture.Text()),
        Digivolutions: Digivolutions());

    /// <summary>Builds fixture forms with predictable form experience thresholds and learnable techniques.</summary>
    /// <returns>A fighter catalogue containing the three party forms and one unlocked form.</returns>
    private static FighterData Fighters()
    {
        DigimonData one = Form(1, [1, 2, 11, 12], [2, 3, 4]);
        DigimonData two = Form(2, [1], []);
        DigimonData three = Form(3, [1], []);
        return new FighterData(
            new Dictionary<int, DigimonData> { [1] = one, [2] = two, [3] = three },
            [one, two, three],
            [new BattleTableEntry(100, 0, 1023, 100, [1, 0, 0], [1, 1, 1, 1, 1], new int[12], 0, [new BattleAction(1, 0, 0)], new BattleAction(0, 0, 0))]);
    }

    /// <summary>Builds the fixture action catalogue including the techniques learned by form one.</summary>
    /// <returns>A deterministic action catalogue.</returns>
    private static TechniqueData Techniques() => new(new Dictionary<int, Dw3Production.Conversion.TechData>
    {
        [1] = Tech(0, 1),
        [2] = Tech(3, 1000),
        [11] = Tech(1, 2),
        [12] = Tech(1, 2),
    });

    /// <summary>Builds one deterministic fixture technique.</summary>
    /// <param name="mp">Technique MP cost.</param>
    /// <param name="power">Technique power.</param>
    /// <returns>A technique record accepted by the production battle factory.</returns>
    private static Dw3Production.Conversion.TechData Tech(int mp, int power) => new(0, mp, power, 0, 0, 128, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1);

    /// <summary>Builds one fixture form by changing only the source skills and their threshold levels.</summary>
    /// <param name="id">Fixture Digimon id.</param>
    /// <param name="skills">Decoded source skill slots.</param>
    /// <param name="skillLevels">Levels required for the learned slots.</param>
    /// <returns>A form record accepted by the production battle factory.</returns>
    private static DigimonData Form(int id, IReadOnlyList<int> skills, IReadOnlyList<int> skillLevels) => new(
        id, [10, 10, 10, 10, 100, 5], new int[7], skills, 0, new int[5], skillLevels, [], 99, 0, 10, 100, 10, 5, 1, new int[6], new int[7], [], id, 0, false);

    /// <summary>Builds a tree that unlocks form two after the used base form reaches level two.</summary>
    /// <returns>Decoded per-partner evolution data.</returns>
    private static DigivolutionData Digivolutions() => new(
        [new PartnerDigivolution(0, 1, [new DigivolutionEntry(0, 2, [new EvolutionNeed(0, 1, 2)], 0, 0)])],
        new DigivolutionBlast([4, 19, 39, 70]));
}
