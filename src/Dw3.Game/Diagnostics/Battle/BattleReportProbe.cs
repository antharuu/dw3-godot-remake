using System.Globalization;
using Dw3Production.Diagnostics.Field;
using Dw3Production.Logging;
using Dw3Production.Session;
using Dw3Production.Simulation;
using Godot;
using NumericsVector2 = System.Numerics.Vector2;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>
/// Validates a natural victory through the production frame loop: the physical action finishes before the
/// battle ends, the reward is settled exactly once at that terminal tick, the report is read page by page with
/// real Cross edges, the field is restored, and a following defeat reaches the title without a second reward.
/// </summary>
internal sealed class BattleReportProbe : BattleProbe
{
    /// <summary>Upper bound of rendered frames for the opening command to reach the terminal phase.</summary>
    private const int VictoryFrames = 1200;

    /// <summary>Upper bound of rendered frames for the victory fade to hand over to the report.</summary>
    private const int ReportEntryFrames = 120;

    /// <summary>Initializes a new instance of the <see cref="BattleReportProbe"/> class.</summary>
    /// <param name="host">Restricted live battle host.</param>
    public BattleReportProbe(IBattleProbeHost host)
        : base(host)
    {
    }

    /// <inheritdoc/>
    protected override string FailureCode => "BATTLE_REPORT_PROBE";

    /// <inheritdoc/>
    protected override async Task ExecuteAsync()
    {
        ProbeRequirement.Require(Host.HasReportData, "rewards or growth state unavailable");
        var captureRoot = Host.PrepareCaptureDirectory("captures");
        VerifyMissingReward();
        var before = await PrepareFieldAsync(captureRoot);
        var settled = await WinAsync(before);
        await EnterReportAsync(captureRoot);
        var walker = new BattleReportPageWalker(Host, captureRoot);
        await walker.WalkAsync();
        VerifyPages(walker.Entries, settled);
        await VerifyReturnAsync(captureRoot, before, settled);
        await VerifyDefeatAsync(captureRoot, settled);
        QuietLog.Info($"battle-report probe PASS encounter={Host.LastBattleEncounter} captures={captureRoot}/report-page-*.png");
    }

    /// <summary>Formats one settled report partner for the checkpoint log.</summary>
    /// <param name="partner">Settled partner.</param>
    /// <returns>Compact description of its gain and used forms.</returns>
    private static string DescribePartner(FightReportPartner partner) =>
        string.Create(CultureInfo.InvariantCulture, $"{partner.FighterId}:+{partner.ExpGained} lv{partner.LevelBefore}->{partner.LevelAfter} slots={partner.FormSlots.Count} learned={partner.LearnedForms.Count}");

    /// <summary>Checks the explicit zero-reward contract for an out-of-range encounter id.</summary>
    private void VerifyMissingReward()
    {
        var absent = Host.ResolveReward(99999);
        ProbeRequirement.Require(
            absent.Exp == 0 && absent.Money == 0 && absent.DigimonExp == 0,
            "an absent reward did not resolve to zero");
    }

    /// <summary>Positions the player at S200 and records the session state the victory must change exactly once.</summary>
    /// <param name="captureRoot">Directory receiving the before capture.</param>
    /// <returns>The field position and session resources before the battle.</returns>
    private async Task<BattleReportSessionSnapshot> PrepareFieldAsync(string captureRoot)
    {
        Host.ApplyS200();
        Host.State.Position = new NumericsVector2(1000, 236);
        Host.State.SetFacing(1);
        Host.State.AnimationSet = 1;
        Host.State.AnimationDirection = 1;
        await Host.CaptureStillAsync(captureRoot, "battle-report-before.png");
        var snapshot = Snapshot();
        QuietLog.Info($"battle-report checkpoint=startup {snapshot}");
        return snapshot;
    }

    /// <summary>Starts encounter 200, attacks once, and steps rendered frames until the action and the battle end.</summary>
    /// <param name="before">Session state before the battle.</param>
    /// <returns>The session state right after the terminal settlement.</returns>
    /// <exception cref="InvalidOperationException">The command, outcome, settlement or reward differs from the contract.</exception>
    private async Task<BattleReportSessionSnapshot> WinAsync(BattleReportSessionSnapshot before)
    {
        Host.StartDebugBattle();
        ProbeRequirement.Require(Host.HasActiveBattle && Host.LastBattleEncounter == 200, "F8 did not start encounter 200");
        await Host.NextFrameDrawn();
        SetKey(Key.Enter, true);
        await StepBattleFrameAsync();
        SetKey(Key.Enter, false);
        ProbeRequirement.Require(Host.Flow.Phase != BattleFlowPhase.PlayerCommand, "the opening Cross did not commit the attack");
        QuietLog.Info($"battle-report checkpoint=input-consumed command=attack phase={Host.Flow.Phase} outcome={Host.Flow.Outcome}");
        var frames = await StepUntilEndedAsync();
        ProbeRequirement.Require(
            Host.HasActiveBattle && Host.Flow.Phase == BattleFlowPhase.Ended && Host.Flow.Outcome == BattleOutcome.Won,
            $"battle did not end in a win frames={frames} phase={Host.Flow.Phase} outcome={Host.Flow.Outcome}");
        var report = Host.BattleReport ?? throw new InvalidOperationException("the terminal tick did not settle a report");
        var input = Host.FightReportInput ?? throw new InvalidOperationException("the terminal tick did not build the fight report input");
        ProbeRequirement.Require(
            report.Exp == 4 && report.Money == 50 && report.DigimonExp == 1,
            $"encounter 200 reward drifted exp={report.Exp} money={report.Money} digimonExp={report.DigimonExp}");
        var settled = Snapshot();
        var leadGain = input.Partners[0].ExpGained;
        ProbeRequirement.Require(settled.LeadExp == before.LeadExp + leadGain, $"lead exp {before.LeadExp}->{settled.LeadExp} differs from the reported +{leadGain}");
        ProbeRequirement.Require(
            settled.Money == Math.Min(before.Money + input.Money, PlayerSession.MoneyMax),
            $"wallet {before.Money}->{settled.Money} differs from the reward {input.Money}");
        QuietLog.Info($"battle-report checkpoint=settled-once frames={frames} requested-money={input.Money} item={input.Item} {settled} partners=[{string.Join(",", input.Partners.Select(DescribePartner))}]");
        return settled;
    }

    /// <summary>Steps rendered battle frames until the terminal phase, logging every phase change of the pending action.</summary>
    /// <returns>The number of frames stepped.</returns>
    private async Task<int> StepUntilEndedAsync()
    {
        var phase = Host.Flow.Phase;
        var frames = 0;
        while (Host.HasActiveBattle && Host.Flow.Phase != BattleFlowPhase.Ended && frames < VictoryFrames)
        {
            await StepBattleFrameAsync();
            frames++;
            if (Host.Flow.Phase != phase)
            {
                QuietLog.Info($"battle-report checkpoint=phase frame={frames} {phase}->{Host.Flow.Phase} outcome={Host.Flow.Outcome} report={Host.BattleReport is not null}");
                phase = Host.Flow.Phase;
            }
        }

        return frames;
    }

    /// <summary>Steps the victory fade until the report renderer owns the screen, then captures the first report frame.</summary>
    /// <param name="captureRoot">Directory receiving the entry capture.</param>
    /// <returns>A task completed after the capture.</returns>
    private async Task EnterReportAsync(string captureRoot)
    {
        var frames = 0;
        while (Host.FightReportController is null && frames < ReportEntryFrames)
        {
            await StepBattleFrameAsync();
            frames++;
        }

        ProbeRequirement.Require(
            Host.FightReportController is not null && Host.IsFightReportVisible,
            $"victory did not enter STFGTREP after {frames} frames");
        ProbeRequirement.Require(frames >= Host.ReportFadeFrames - 1, $"report entered before the {Host.ReportFadeFrames}-frame victory fade frames={frames}");
        await BattleProbeCapture.CaptureRawAsync(Host, captureRoot, "report-entry.png");
        QuietLog.Info($"battle-report checkpoint=report-entry fade-frames={frames} {Snapshot()}");
    }

    /// <summary>Checks the confirmed pages against the settled report: an EXP page first, then money, then the item when one was won.</summary>
    /// <param name="entries">Confirmed message entries in reading order.</param>
    /// <param name="settled">Session state after settlement.</param>
    private void VerifyPages(IReadOnlyList<int> entries, BattleReportSessionSnapshot settled)
    {
        var listed = string.Join(",", entries);
        ProbeRequirement.Require(entries.Count > 0 && entries[0] == 2, $"the first report page is not the EXP message entries=[{listed}]");
        ProbeRequirement.Require(entries.Contains(10), $"the report skipped the money page entries=[{listed}]");
        ProbeRequirement.Require(entries.Contains(11) == (settled.Item != 0), $"item page presence differs from item={settled.Item} entries=[{listed}]");
        ProbeRequirement.Require(Snapshot() == settled, $"reading the report changed the settled session {settled} -> {Snapshot()}");
    }

    /// <summary>Verifies the report handed control back to the unchanged field without a second reward.</summary>
    /// <param name="captureRoot">Directory receiving the field capture.</param>
    /// <param name="before">Session state before the battle, holding the field position.</param>
    /// <param name="settled">Session state after settlement.</param>
    /// <returns>A task completed after the restored-field capture.</returns>
    private async Task VerifyReturnAsync(string captureRoot, BattleReportSessionSnapshot before, BattleReportSessionSnapshot settled)
    {
        ProbeRequirement.Require(
            !Host.HasActiveBattle && !Host.State.BattleActive && !Host.IsInTitle,
            "victory did not return to the field");
        ProbeRequirement.Require(
            Host.State.Position == before.Position,
            $"field position changed after the battle before={before.Position} after={Host.State.Position}");
        ProbeRequirement.Require(Snapshot() == settled, $"returning to the field changed the settled session {settled} -> {Snapshot()}");
        await Host.CaptureStillAsync(captureRoot, "battle-report-after.png");
        QuietLog.Info($"battle-report checkpoint=field-return {Snapshot()}");
    }

    /// <summary>Starts a subsequent battle, forces its loss, and verifies the title route without any reward.</summary>
    /// <param name="captureRoot">Directory receiving the title capture.</param>
    /// <param name="settled">Session state after the victory settlement.</param>
    /// <returns>A task that completes after the title-screen capture.</returns>
    private async Task VerifyDefeatAsync(string captureRoot, BattleReportSessionSnapshot settled)
    {
        Host.StartDebugBattle();
        ProbeRequirement.Require(Host.HasActiveBattle, "the defeat battle did not start");
        Host.Flow.EndBattle(BattleOutcome.Lost);
        var guard = 0;
        while (Host.HasActiveBattle && guard++ < 128)
        {
            Host.TickTestSimulation();
        }

        ProbeRequirement.Require(!Host.HasActiveBattle && !Host.State.BattleActive, "the defeat battle did not end");
        ProbeRequirement.Require(Host.IsInTitle, "a defeat did not reach the title screen");
        ProbeRequirement.Require(Host.IsTitleVisible, "the title screen is not visible on defeat");
        var after = Snapshot();
        ProbeRequirement.Require(after.LeadExp == settled.LeadExp && after.Money == settled.Money, $"the defeat paid a reward {settled} -> {after}");
        await Host.CaptureStillAsync(captureRoot, "battle-report-title.png");
        QuietLog.Info($"battle-report defeat return-to-title inTitle={Host.IsInTitle} titleVisible={Host.IsTitleVisible} {after}");
    }

    /// <summary>Advances one production battle tick, presents the battle view while the battle renderer owns the screen, and waits for the drawn frame.</summary>
    /// <returns>A task completed after the frame was drawn.</returns>
    private async Task StepBattleFrameAsync()
    {
        Host.TickTestSimulation();
        if (Host.HasActiveBattle && Host.FightReportController is null)
        {
            Host.Renderer.Present(Host.BuildBattleView());
        }

        await Host.NextFrameDrawn();
    }

    /// <summary>Reads the session resources the report must change exactly once.</summary>
    /// <returns>The current snapshot.</returns>
    /// <exception cref="InvalidOperationException">The session has no lead partner growth.</exception>
    private BattleReportSessionSnapshot Snapshot()
    {
        var lead = Host.PartyGrowth is [PartnerGrowthState growth, ..]
            ? growth
            : throw new InvalidOperationException("the session has no lead partner growth");
        var item = Host.FightReportInput?.Item ?? 0;
        return new BattleReportSessionSnapshot(Host.State.Position, lead.Exp, lead.Level, Host.SessionMoney ?? 0, item, Host.SessionItemCount(item));
    }

    /// <summary>Injects one native key state without advancing simulation.</summary>
    /// <param name="key">Godot key code to inject.</param>
    /// <param name="pressed">Whether the event is a press or release.</param>
    private void SetKey(Key key, bool pressed) => Host.Input.Handle(new InputEventKey { Keycode = key, Pressed = pressed });
}
