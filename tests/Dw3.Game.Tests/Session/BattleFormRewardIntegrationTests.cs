using Dw3Production;
using Dw3Production.Conversion;
using Dw3Production.Session;
using Dw3Production.Simulation;
using Xunit;

namespace Dw3.Game.Tests.Session;

/// <summary>
/// Drives fabricated battles through the production settlement seam and the real report controller, and checks
/// form rows, report messages and next-battle availability against a ledger derived from the original rules.
/// </summary>
/// <remarks>
/// Ledger, partner level 25, ExpLevel 10, reward 30 unless stated: n = 300 / 25 = 12. One used slot over the
/// fought party gives 10 (cap), two give 7, three give 4. A form needs 10 cumulative experience per level up to
/// ExpLevel. The unlock entry acquires form 8 once form 5 reaches level 2.
/// </remarks>
public sealed class BattleFormRewardIntegrationTests
{
    /// <summary>Two partners fighting as the same form id count as two used slots, the third partner gets nothing.</summary>
    [Fact]
    public void WonBattle_PartnersSharingOneFormIdCountTwoUsedSlots()
    {
        PlayerSession session = FormProgressFixture.Session(level: 25);
        BattleFlowController flow = FormProgressFixture.Build(session);
        FormProgressFixture.Digivolve(flow, 5);
        FormProgressFixture.Switch(flow, 1);
        FormProgressFixture.FightAs(flow, [5]);

        BattleOutcomeResolution resolution = Resolve(session, flow, FormProgressFixture.Victory(30));

        Assert.Equal(7, Partner(session, 0).FormProgress(5).Exp);
        Assert.Equal(7, Partner(session, 1).FormProgress(5).Exp);
        Assert.Equal(0, Partner(session, 2).FormProgress(5).Exp);
        Assert.Equal((0, 5, 7), Row(resolution, 0));
        Assert.Equal((0, 5, 7), Row(resolution, 1));
        Assert.Empty(resolution.FightReport.Partners[2].FormSlots);
    }

    /// <summary>A partner that fought but is knocked out at the end neither receives nor counts its used slot.</summary>
    [Fact]
    public void WonBattle_KnockedOutPartnerIsLeftOutOfTheUsedCount()
    {
        PlayerSession session = FormProgressFixture.Session(level: 25);
        BattleFlowController flow = FormProgressFixture.Build(session);
        FormProgressFixture.Digivolve(flow, 5);
        FormProgressFixture.Switch(flow, 1);
        FormProgressFixture.FightAs(flow, [5]);
        Assert.IsType<BattleFighter>(flow.State.Players[0]).Hp = 0;

        BattleOutcomeResolution resolution = Resolve(session, flow, FormProgressFixture.Victory(30));

        Assert.Equal(0, Partner(session, 0).FormProgress(5).Exp);
        Assert.Empty(resolution.FightReport.Partners[0].FormSlots);
        Assert.Equal(10, Partner(session, 1).FormProgress(5).Exp);
    }

    /// <summary>Fighting only as the base form gives no form row, and the report keeps exactly three equipped slots.</summary>
    [Fact]
    public void WonBattle_BaseFormIsNeverAReportSlot()
    {
        PlayerSession session = FormProgressFixture.Session(level: 25);
        BattleFlowController flow = FormProgressFixture.Build(session);
        FormProgressFixture.FightAs(flow, []);

        BattleOutcomeResolution resolution = Resolve(session, flow, FormProgressFixture.Victory(30));
        ReportRun run = RunReport(resolution);

        FightReportPartner report = resolution.FightReport.Partners[0];
        Assert.Equal([5, 6, 7], report.SlotDigimonIds);
        Assert.Empty(report.FormSlots);
        Assert.Equal(0, Partner(session, 0).FormProgress(1).Exp);
        Assert.Equal([5, 6, 7], run.SlotIds);
        Assert.DoesNotContain(run.Logs, line => line.StartsWith("report-form-slot", StringComparison.Ordinal));
        Assert.DoesNotContain(run.Messages, message => message.Entry is >= 4 and <= 9);
    }

    /// <summary>The third equipped slot receives its share and its own level blink.</summary>
    [Fact]
    public void WonBattle_ThirdEquippedSlotReceivesItsShare()
    {
        PlayerSession session = FormProgressFixture.Session(level: 25);
        BattleFlowController flow = FormProgressFixture.Build(session);
        FormProgressFixture.FightAs(flow, [7]);

        BattleOutcomeResolution resolution = Resolve(session, flow, FormProgressFixture.Victory(30));
        ReportRun run = RunReport(resolution);

        Assert.Equal((2, 7, 10), Row(resolution, 0));
        Assert.Equal((2, 10), (Partner(session, 0).FormProgress(7).Level, Partner(session, 0).FormProgress(7).Exp));
        Assert.True(run.SlotExpApplied[2]);
        Assert.True(run.SlotBlinkFinished[2]);
        Assert.False(run.SlotExpApplied[0]);
        Assert.DoesNotContain(run.Logs, line => line.StartsWith("report-form-slot", StringComparison.Ordinal));
    }

    /// <summary>
    /// A form below its next level gets its share without any level, skill or unlock message. Ledger: 30 and level
    /// 25 over three used slots give 4, short of the 10 needed.
    /// </summary>
    [Fact]
    public void WonBattle_ShareWithoutLevelShowsNoFormMessage()
    {
        PlayerSession session = FormProgressFixture.Session(level: 25);
        BattleFlowController flow = FormProgressFixture.Build(session);
        FormProgressFixture.FightAs(flow, [5, 6, 7]);

        BattleOutcomeResolution resolution = Resolve(session, flow, FormProgressFixture.Victory(30, UnlockAtLevel2()));
        ReportRun run = RunReport(resolution);

        Assert.Equal(3, resolution.FightReport.Partners[0].FormSlots.Count);
        Assert.All(resolution.FightReport.Partners[0].FormSlots, row => Assert.Equal((4, 1, 1), (row.ExpGained, row.LevelBefore, row.LevelAfter)));
        Assert.Empty(resolution.FightReport.Partners[0].LearnedForms);
        Assert.Equal([true, true, true], run.SlotExpApplied);
        Assert.DoesNotContain(run.Messages, message => message.Entry is >= 4 and <= 9);
    }

    /// <summary>
    /// Plays one level-up with an acquisition, a mastery and an unlock in the original order: level (4), acquired
    /// skill 12 (5), mastered skill 11 (6), new form 8 (8), then the notice (9) since four forms are acquired.
    /// </summary>
    [Fact]
    public void WonBattle_ReportPlaysLevelAcquisitionMasteryAndUnlockInOrder()
    {
        PlayerSession session = FormProgressFixture.Session(level: 25);
        PartnerFormProgress five = Partner(session, 0).FormProgress(5);
        five.Level = 4;
        five.Exp = 30;
        five.AcquireSkill(0, FormProgressFixture.AcquiredTechnique);
        FighterData fighters = FormProgressFixture.Fighters(skillLevels: [2, 5, 99], knownLevels: [5, 99, 99]);
        BattleFlowController flow = FormProgressFixture.Build(session, fighters);
        FormProgressFixture.FightAs(flow, [5]);

        DigivolutionEntry[] tree = [new DigivolutionEntry(0, FormProgressFixture.UnlockedForm, [new EvolutionNeed(0, 5, 5)], 0, 0)];
        BattleOutcomeResolution resolution = Resolve(session, flow, FormProgressFixture.Victory(30, tree, fighters));
        ReportRun run = RunReport(resolution);

        FightReportFormSlot row = Assert.Single(resolution.FightReport.Partners[0].FormSlots);
        Assert.Equal((30, 10, 4, 5), (row.ExpBefore, row.ExpGained, row.LevelBefore, row.LevelAfter));
        Assert.Equal([FormProgressFixture.LateTechnique], row.AcquiredTechniques);
        Assert.Equal([FormProgressFixture.AcquiredTechnique], row.MasteredTechniques);
        Assert.Equal(
            ["4:", "5:12", "6:11", "8:8", "9:"],
            run.Messages.Where(message => message.Entry is >= 4 and <= 9).Select(message => $"{message.Entry}:{string.Join(",", message.Substitutions)}"));
        Assert.Equal(4, resolution.FightReport.Partners[0].AcquiredFormCount);
    }

    /// <summary>
    /// The notice after an unlock depends on four or more acquired forms besides the base, not on whether the new
    /// form found a slot.
    /// </summary>
    /// <param name="known">Acquired forms besides the base before the battle.</param>
    /// <param name="equipped">Equipped slots before the battle.</param>
    /// <param name="expectedEquipped">Equipped slots after the battle.</param>
    /// <param name="notice">Whether the notice is expected.</param>
    [Theory]
    [InlineData(new[] { 5, 6, 7 }, new[] { 5, 6 }, new[] { 5, 6, 8 }, true)] // free slot, but four acquired
    [InlineData(new[] { 5 }, new[] { 5 }, new[] { 5, 8 }, false)] // free slot, two acquired
    [InlineData(new[] { 5, 6, 7 }, new[] { 5, 6, 7 }, new[] { 5, 6, 7 }, true)] // full slots, four acquired
    [InlineData(new[] { 5, 6, 7, 8 }, new[] { 5, 6, 7 }, new[] { 5, 6, 7 }, false)] // four acquired, no unlock
    public void WonBattle_NoticeFollowsAcquiredFormCountAfterAnUnlock(int[] known, int[] equipped, int[] expectedEquipped, bool notice)
    {
        PlayerSession session = FormProgressFixture.Session(level: 25, known, equipped);
        BattleFlowController flow = FormProgressFixture.Build(session);
        FormProgressFixture.FightAs(flow, [5]);

        BattleOutcomeResolution resolution = Resolve(session, flow, FormProgressFixture.Victory(30, UnlockAtLevel2()));
        ReportRun run = RunReport(resolution);

        Assert.Equal(expectedEquipped, Partner(session, 0).EquippedForms);
        Assert.Equal(equipped, resolution.FightReport.Partners[0].SlotDigimonIds);
        Assert.Equal(notice, run.Messages.Any(message => message.Entry == 9));
        Assert.Equal(known.Contains(FormProgressFixture.UnlockedForm) ? 0 : 1, run.Messages.Count(message => message.Entry == 8));
    }

    /// <summary>The next battle offers an acquired skill to its own form only, and the unlocked equipped form.</summary>
    [Fact]
    public void NextBattle_OffersFormLocalSkillsAndTheUnlockedForm()
    {
        PlayerSession session = FormProgressFixture.Session(level: 25, [5, 6], [5, 6]);
        BattleFlowController first = FormProgressFixture.Build(session);
        FormProgressFixture.FightAs(first, [5]);
        Resolve(session, first, FormProgressFixture.Victory(30, UnlockAtLevel2()));

        BattleFlowController second = FormProgressFixture.Build(session);

        Assert.Contains(second.PlayerTechniquesByForm[5], technique => technique.Id == FormProgressFixture.AcquiredTechnique);
        Assert.DoesNotContain(second.PlayerTechniquesByForm[6], technique => technique.Id == FormProgressFixture.AcquiredTechnique);
        Assert.Contains(FormProgressFixture.UnlockedForm, second.PlayerDigivolutions);
        Assert.Equal([5, 6, FormProgressFixture.UnlockedForm], Partner(session, 0).EquippedForms);
    }

    /// <summary>Stepping the finished report changes nothing and the settlement credited the form once.</summary>
    [Fact]
    public void WonBattle_FinishedReportStepsDoNotCreditAgain()
    {
        PlayerSession session = FormProgressFixture.Session(level: 25);
        BattleFlowController flow = FormProgressFixture.Build(session);
        FormProgressFixture.FightAs(flow, [5]);
        BattleOutcomeResolution resolution = Resolve(session, flow, FormProgressFixture.Victory(30));
        var report = new FightReportController(resolution.FightReport);

        for (int frame = 0; frame < 20_000 && !report.View.Done; frame++)
        {
            report.Step(crossPressedEdge: true);
        }

        for (int extra = 0; extra < 100; extra++)
        {
            report.Step(crossPressedEdge: true);
        }

        Assert.True(report.View.Done);
        Assert.Equal(10, Partner(session, 0).FormProgress(5).Exp);
    }

    /// <summary>Builds the unlock entry acquiring form 8 once form 5 reaches level 2.</summary>
    /// <returns>The first partner's evolution entries.</returns>
    private static DigivolutionEntry[] UnlockAtLevel2() => [new DigivolutionEntry(0, FormProgressFixture.UnlockedForm, [new EvolutionNeed(0, 5, 2)], 0, 0)];

    /// <summary>Resolves the terminal battle through the production seam.</summary>
    /// <param name="session">Persistent session.</param>
    /// <param name="flow">Won flow.</param>
    /// <param name="victory">Victory inputs.</param>
    /// <returns>The delivered resolution.</returns>
    private static BattleOutcomeResolution Resolve(PlayerSession session, BattleFlowController flow, BattleVictoryContext victory) =>
        GameHost.BattleSetupCoordinator.ResolveOutcome(session, flow, () => victory) ?? throw new InvalidOperationException("battle was not won");

    /// <summary>Returns one session partner.</summary>
    /// <param name="session">Persistent session.</param>
    /// <param name="index">Party slot.</param>
    /// <returns>The partner.</returns>
    private static PlayerSessionPartner Partner(PlayerSession session, int index) => Assert.IsType<PlayerSessionPartner>(session.Partners[index]);

    /// <summary>Returns the only row of one report partner.</summary>
    /// <param name="resolution">Delivered resolution.</param>
    /// <param name="index">Party slot.</param>
    /// <returns>Slot, form and experience gained.</returns>
    private static (int Slot, int Form, int Gained) Row(BattleOutcomeResolution resolution, int index)
    {
        FightReportFormSlot row = Assert.Single(resolution.FightReport.Partners[index].FormSlots);
        return (row.Slot, row.DigimonId, row.ExpGained);
    }

    /// <summary>Runs the real report controller to its end while pressing Cross, collecting messages and slot state.</summary>
    /// <param name="resolution">Delivered resolution.</param>
    /// <returns>The observed run.</returns>
    private static ReportRun RunReport(BattleOutcomeResolution resolution)
    {
        var report = new FightReportController(resolution.FightReport);
        var run = new ReportRun();
        string? lastKey = null;
        for (int frame = 0; frame < 20_000 && !report.View.Done; frame++)
        {
            FightReportMessage? message = report.View.Message;
            string? key = message is null ? null : $"{message.Entry}:{string.Join(",", message.Substitutions)}";
            if (message is not null && key != lastKey)
            {
                run.Messages.Add(message);
            }

            lastKey = key;
            FightReportSlotView[] slots = [.. report.View.Partners[0].Slots];
            run.SlotIds = [.. slots.Where(slot => slot.Visible).Select(slot => slot.DigimonId)];
            for (int slot = 0; slot < slots.Length; slot++)
            {
                run.SlotExpApplied[slot] |= slots[slot].ExpApplied;
                run.SlotBlinkFinished[slot] |= slots[slot].BlinkFrame == 10;
            }

            report.Step(crossPressedEdge: true);
        }

        Assert.True(report.View.Done);
        run.Logs = [.. report.Logs];
        return run;
    }

    /// <summary>Observations collected while running a report.</summary>
    private sealed class ReportRun
    {
        /// <summary>Gets the distinct messages in the order they started.</summary>
        public List<FightReportMessage> Messages { get; } = [];

        /// <summary>Gets or sets the visible slot ids of the first panel.</summary>
        public int[] SlotIds { get; set; } = [];

        /// <summary>Gets whether each slot of the first panel showed applied experience.</summary>
        public bool[] SlotExpApplied { get; } = new bool[3];

        /// <summary>Gets whether each slot of the first panel finished its level blink.</summary>
        public bool[] SlotBlinkFinished { get; } = new bool[3];

        /// <summary>Gets or sets the report diagnostics.</summary>
        public string[] Logs { get; set; } = [];
    }
}
