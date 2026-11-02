using Dw3Production.Diagnostics.Field;
using Dw3Production.Logging;
using Dw3Production.Simulation;
using Godot;
using NumericsVector2 = System.Numerics.Vector2;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>Validates victory rewards, report paging, field restoration, and defeat-to-title routing.</summary>
internal sealed class BattleReportProbe : BattleProbe
{
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
        var (beforePosition, kotemonExpBefore) = await PrepareFieldAsync(captureRoot);
        await WinAndShowReportAsync(captureRoot, kotemonExpBefore);
        await AdvanceReportAndReturnAsync(captureRoot, beforePosition);
        await VerifyDefeatAsync(captureRoot);
        QuietLog.Info($"battle-report probe PASS encounter={Host.LastBattleEncounter} captures={captureRoot}/battle-report-*.png");
    }

    /// <summary>Checks the explicit zero-reward contract for an out-of-range encounter id.</summary>
    private void VerifyMissingReward()
    {
        var absent = Host.ResolveReward(99999);
        ProbeRequirement.Require(
            absent.Exp == 0 && absent.Money == 0 && absent.DigimonExp == 0,
            "an absent reward did not resolve to zero");
    }

    /// <summary>Positions the player at S200 and captures state needed after the report closes.</summary>
    /// <param name="captureRoot">Directory receiving the before capture.</param>
    /// <returns>The expected restored position and Kotemon's pre-battle experience.</returns>
    private async Task<(NumericsVector2 Position, int Exp)> PrepareFieldAsync(string captureRoot)
    {
        Host.ApplyS200();
        Host.State.Position = new NumericsVector2(1000, 236);
        Host.State.SetFacing(1);
        Host.State.AnimationSet = 1;
        Host.State.AnimationDirection = 1;
        await Host.CaptureStillAsync(captureRoot, "battle-report-before.png");
        var partyGrowth = Host.PartyGrowth!;
        return (Host.State.Position, partyGrowth[0].Exp);
    }

    /// <summary>Wins encounter 200 and proves its sourced reward before displaying the report pages.</summary>
    /// <param name="captureRoot">Directory receiving report captures.</param>
    /// <param name="kotemonExpBefore">Experience recorded before the battle.</param>
    /// <returns>A task that completes once the EXP report page is visible.</returns>
    private async Task WinAndShowReportAsync(string captureRoot, int kotemonExpBefore)
    {
        Host.StartDebugBattle();
        ProbeRequirement.Require(Host.HasActiveBattle && Host.LastBattleEncounter == 200, "F8 did not start encounter 200");
        SetKey(Key.Enter, true);
        Host.TickTestSimulation();
        SetKey(Key.Enter, false);
        var guard = 0;
        while (Host.HasActiveBattle && Host.Flow.Phase != BattleFlowPhase.Ended && guard++ < 128)
        {
            Host.TickTestSimulation();
        }

        ProbeRequirement.Require(
            Host.HasActiveBattle && Host.Flow.Outcome == BattleOutcome.Won,
            $"battle did not win outcome={Host.Flow.Outcome}");
        Host.TickTestSimulation();
        var report = Host.BattleReport ?? throw new InvalidOperationException("victory did not resolve a report");
        ProbeRequirement.Require(
            report.Exp == 4 && report.Money == 50 && report.DigimonExp == 1,
            $"encounter 200 reward drifted exp={report.Exp} money={report.Money} digimonExp={report.DigimonExp}");
        ProbeRequirement.Require(
            Host.PartyGrowth![0].Exp == kotemonExpBefore + 4,
            $"the fought partner did not gain the exp share exp={Host.PartyGrowth[0].Exp}");
        for (var i = 0; i < Host.ReportFadeFrames + 20; i++)
        {
            Host.TickTestSimulation();
        }

        ProbeRequirement.Require(
            Host.FightReportController is not null && Host.IsFightReportVisible,
            "victory did not enter STFGTREP");
        await BattleProbeCapture.CaptureAsync(Host, captureRoot, "report-panels.png");
        guard = 0;
        while (Host.FightReportController!.View.Message?.Entry != 2 && guard++ < 128)
        {
            Host.TickTestSimulation();
        }

        ProbeRequirement.Require(Host.FightReportController.View.Message?.Entry == 2, "report did not reach the EXP message");
        await BattleProbeCapture.CaptureAsync(Host, captureRoot, "report-exp.png");
        QuietLog.Info($"battle-report victory exp={report.Exp} money={report.Money} digimonExp={report.DigimonExp} partners=[{string.Join(",", report.Partners.Select(partner => $"{partner.Name}:+{partner.ExpGained}"))}]");
        HoldReportWithoutInput();
    }

    /// <summary>Proves that the current report page remains open until Cross is pressed.</summary>
    private void HoldReportWithoutInput()
    {
        for (var i = 0; i < 600; i++)
        {
            Host.TickTestSimulation();
        }

        ProbeRequirement.Require(
            Host.HasActiveBattle && Host.FightReportController is not null,
            "victory report closed without Cross before 600 steps");
    }

    /// <summary>Advances report pages through the source input path and verifies return to the original field state.</summary>
    /// <param name="captureRoot">Directory receiving the report and field captures.</param>
    /// <param name="beforePosition">Position expected after victory report closure.</param>
    /// <returns>A task that completes after the restored-field capture.</returns>
    private async Task AdvanceReportAndReturnAsync(string captureRoot, NumericsVector2 beforePosition)
    {
        InjectBattleKey(Key.Enter);
        var guard = 0;
        while (Host.FightReportController is not null && Host.FightReportController.View.Message?.Entry != 10 && guard++ < 256)
        {
            Host.TickTestSimulation();
        }

        ProbeRequirement.Require(
            Host.FightReportController is not null && Host.FightReportController.View.Message?.Entry == 10,
            "report did not reach the money page");
        await BattleProbeCapture.CaptureAsync(Host, captureRoot, "report-money.png");
        guard = 0;
        while (Host.HasActiveBattle && guard++ < 256)
        {
            if (guard % 12 == 0)
            {
                InjectBattleKey(Key.Enter);
            }
            else
            {
                Host.TickTestSimulation();
            }
        }

        ProbeRequirement.Require(
            !Host.HasActiveBattle && !Host.State.BattleActive && !Host.IsInTitle,
            "victory did not return to the field");
        ProbeRequirement.Require(
            Host.State.Position == beforePosition,
            $"field position changed after the battle before={beforePosition} after={Host.State.Position}");
        await Host.CaptureStillAsync(captureRoot, "battle-report-after.png");
    }

    /// <summary>Forces a loss and verifies that it ends at the visible title screen rather than the field.</summary>
    /// <param name="captureRoot">Directory receiving the title capture.</param>
    /// <returns>A task that completes after the title-screen capture.</returns>
    private async Task VerifyDefeatAsync(string captureRoot)
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
        await Host.CaptureStillAsync(captureRoot, "battle-report-title.png");
        QuietLog.Info($"battle-report defeat return-to-title inTitle={Host.IsInTitle} titleVisible={Host.IsTitleVisible}");
    }

    /// <summary>Injects a complete native press edge and its paired release through battle simulation.</summary>
    /// <param name="key">Godot key code to inject.</param>
    private void InjectBattleKey(Key key)
    {
        SetKey(key, true);
        Host.TickTestSimulation();
        SetKey(key, false);
        Host.TickTestSimulation();
    }

    /// <summary>Injects one native key state without advancing simulation.</summary>
    /// <param name="key">Godot key code to inject.</param>
    /// <param name="pressed">Whether the event is a press or release.</param>
    private void SetKey(Key key, bool pressed) => Host.Input.Handle(new InputEventKey { Keycode = key, Pressed = pressed });
}
