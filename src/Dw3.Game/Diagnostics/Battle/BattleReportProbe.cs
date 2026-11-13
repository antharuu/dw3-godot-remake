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
/// battle ends, the terminal tick only snapshots the reward, the report is read page by page with real Cross edges
/// while the experience is credited once the EXP page is confirmed, the money when its page opens and the item when
/// its page opens, the field is restored, and a following defeat reaches the title without a second reward.
/// </summary>
internal sealed class BattleReportProbe : BattleProbe
{
    /// <summary>Upper bound of rendered frames for the opening command to reach the terminal phase.</summary>
    private const int VictoryFrames = 1200;

    /// <summary>Upper bound of rendered frames for the victory fade to hand over to the report.</summary>
    private const int ReportEntryFrames = 120;

    /// <summary>The source maximum of one item's stock.</summary>
    private const int StockMax = 99;

    /// <summary>The source maximum of a partner's experience.</summary>
    private const int ExpMax = 999999;

    private readonly BattleReportFixture _fixture = BattleReportFixture.FromEnvironment();
    private BattleReportSessionSnapshot _before;
    private FightReportInput? _awards;
    private bool _leadExpPageSeen;

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
        _before = await PrepareFieldAsync(captureRoot);
        _awards = await WinAsync(_before);
        await EnterReportAsync(captureRoot);
        var walker = new BattleReportPageWalker(Host, captureRoot, ObservePage);
        await walker.WalkAsync();
        var settled = VerifyPages(walker.Entries, _awards);
        await VerifyReturnAsync(captureRoot, _before, settled);
        await VerifyDefeatAsync(captureRoot, settled);
        QuietLog.Info($"battle-report probe PASS fixture={_fixture.Name} encounter={Host.LastBattleEncounter} captures={captureRoot}/report-page-*.png");
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
        _fixture.PrepareSession(Host);
        await Host.CaptureStillAsync(captureRoot, "battle-report-before.png");
        var snapshot = Snapshot(_fixture.PrizeItem);
        QuietLog.Info($"battle-report checkpoint=startup fixture={_fixture} {snapshot}");
        foreach (string line in BattleReportFixture.DescribeParty(Host))
        {
            QuietLog.Info($"battle-report checkpoint=party {line}");
        }

        return snapshot;
    }

    /// <summary>Starts the fixture's encounter, attacks once, and steps rendered frames until the action and the battle end.</summary>
    /// <param name="before">Session state before the battle.</param>
    /// <returns>The award snapshot the report plays.</returns>
    /// <exception cref="InvalidOperationException">The command, outcome or reward differs from the contract, or the terminal tick credited the session.</exception>
    private async Task<FightReportInput> WinAsync(BattleReportSessionSnapshot before)
    {
        _fixture.StartBattle(Host);
        ProbeRequirement.Require(Host.HasActiveBattle && Host.LastBattleEncounter == _fixture.Encounter, $"the battle did not start encounter {_fixture.Encounter}");
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
        var report = Host.BattleReport ?? throw new InvalidOperationException("the terminal tick did not resolve a report");
        var input = Host.FightReportInput ?? throw new InvalidOperationException("the terminal tick did not build the fight report input");
        var reward = Host.ResolveReward(_fixture.Encounter);
        ProbeRequirement.Require(
            report.Exp == reward.Exp && report.DigimonExp == reward.DigimonExp && input.Item == _fixture.PrizeItem,
            $"encounter {_fixture.Encounter} reward drifted exp={report.Exp} money={report.Money} digimonExp={report.DigimonExp} item={input.Item}");
        var terminal = Snapshot(input.Item);
        ProbeRequirement.Require(
            terminal.LeadExp == before.LeadExp && terminal.Money == before.Money && terminal.ItemCount == before.ItemCount,
            $"the terminal tick credited the session before the report {before} -> {terminal}");
        QuietLog.Info($"battle-report checkpoint=terminal-snapshot frames={frames} requested-money={input.Money} item={input.Item} {terminal} partners=[{string.Join(",", input.Partners.Select(DescribePartner))}]");
        return input;
    }

    /// <summary>Checks the session at each report page against the original's credit stages.</summary>
    /// <param name="entry">Waiting or confirmed message entry.</param>
    /// <param name="confirmed">Whether the page was just confirmed.</param>
    /// <exception cref="InvalidOperationException">A stage was credited early, late or twice.</exception>
    private void ObservePage(int entry, bool confirmed)
    {
        var awards = _awards ?? throw new InvalidOperationException("no award snapshot");
        var now = Snapshot(awards.Item);
        QuietLog.Info($"battle-report checkpoint=session entry={entry} {(confirmed ? "confirmed" : "waiting")} {now}");
        if (entry == 2 && !_leadExpPageSeen)
        {
            int expected = confirmed ? Math.Min(ExpMax, _before.LeadExp + awards.Partners[0].ExpGained) : _before.LeadExp;
            ProbeRequirement.Require(now.LeadExp == expected, $"lead exp {now.LeadExp} differs from {expected} on its EXP page confirmed={confirmed}");
            _leadExpPageSeen = confirmed;
        }

        if (confirmed)
        {
            // Frames stepped while Cross is held or released may already reach the next stage, so stages are checked on waiting pages.
            return;
        }

        int money = entry is 10 or 11 ? Math.Min(PlayerSession.MoneyMax, _before.Money + awards.Money) : _before.Money;
        ProbeRequirement.Require(now.Money == money, $"wallet {now.Money} differs from {money} while entry {entry} waits");
        int stock = entry == 11 ? Math.Min(StockMax, _before.ItemCount + 1) : _before.ItemCount;
        ProbeRequirement.Require(awards.Item == 0 || now.ItemCount == stock, $"stock {now.ItemCount} differs from {stock} while entry {entry} waits");
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

    /// <summary>Checks the confirmed pages against the awards and the session the report credited.</summary>
    /// <param name="entries">Confirmed message entries in reading order.</param>
    /// <param name="awards">Award snapshot the report played.</param>
    /// <returns>The session state after the report.</returns>
    private BattleReportSessionSnapshot VerifyPages(IReadOnlyList<int> entries, FightReportInput awards)
    {
        var listed = string.Join(",", entries);
        ProbeRequirement.Require(entries.Count > 0 && entries[0] == 2, $"the first report page is not the EXP message entries=[{listed}]");
        ProbeRequirement.Require(entries.Contains(10), $"the report skipped the money page entries=[{listed}]");
        ProbeRequirement.Require(entries.Contains(11) == (awards.Item != 0), $"item page presence differs from item={awards.Item} entries=[{listed}]");
        var settled = Snapshot(awards.Item);
        ProbeRequirement.Require(
            settled.LeadExp == Math.Min(ExpMax, _before.LeadExp + awards.Partners[0].ExpGained)
                && settled.Money == Math.Min(PlayerSession.MoneyMax, _before.Money + awards.Money)
                && (awards.Item == 0 || settled.ItemCount == Math.Min(StockMax, _before.ItemCount + 1)),
            $"the report credited {_before} -> {settled} for exp+{awards.Partners[0].ExpGained} money+{awards.Money} item={awards.Item}");
        QuietLog.Info($"battle-report checkpoint=settled-once {settled}");
        return settled;
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
    private BattleReportSessionSnapshot Snapshot() => Snapshot(_awards?.Item ?? _fixture.PrizeItem);

    /// <summary>Reads the session resources, counting the stock of one item.</summary>
    /// <param name="item">Item whose stock is read, 0 for none.</param>
    /// <returns>The current snapshot.</returns>
    /// <exception cref="InvalidOperationException">The session has no lead partner growth.</exception>
    private BattleReportSessionSnapshot Snapshot(int item)
    {
        var lead = Host.PartyGrowth is [PartnerGrowthState growth, ..]
            ? growth
            : throw new InvalidOperationException("the session has no lead partner growth");
        return new BattleReportSessionSnapshot(Host.State.Position, lead.Exp, lead.Level, Host.SessionMoney ?? 0, item, Host.SessionItemCount(item));
    }

    /// <summary>Injects one native key state without advancing simulation.</summary>
    /// <param name="key">Godot key code to inject.</param>
    /// <param name="pressed">Whether the event is a press or release.</param>
    private void SetKey(Key key, bool pressed) => Host.Input.Handle(new InputEventKey { Keycode = key, Pressed = pressed });
}
