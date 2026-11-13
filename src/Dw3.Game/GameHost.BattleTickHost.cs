using Dw3Production.Simulation;

namespace Dw3Production;

/// <summary>Contains GameHost's battle-tick host contract implementation.</summary>
public partial class GameHost
{
    /// <inheritdoc/>
    void IBattleTickHost.TickFightReport()
    {
        _fightReportController!.Step(_input.State.ConsumeActionPressed());
        _fightReportRenderer?.Present(_fightReportController.View, BattleText(), BuildReportNames(), BuildReportItemNames());
        if (_fightReportController.View.Done)
        {
            EndBattle();
        }
    }

    /// <inheritdoc/>
    void IBattleTickHost.ActivatePlayerCamera()
    {
        _battleRenderer?.SetCommandMenuCamera();
        _battlePlayerCameraActive = true;
    }

    /// <inheritdoc/>
    void IBattleTickHost.DeactivatePlayerCamera() => _battlePlayerCameraActive = false;

    /// <inheritdoc/>
    void IBattleTickHost.TickBattleMenu() => TickBattleMenu();

    /// <inheritdoc/>
    void IBattleTickHost.EnemyAct() => BattleFlow.EnemyAct(_battleRng);

    /// <inheritdoc/>
    void IBattleTickHost.TickActionSequence() => TickBattleActionSequence();

    /// <inheritdoc/>
    void IBattleTickHost.RefreshModels() => RefreshBattlePresentationModels();

    /// <inheritdoc/>
    bool IBattleTickHost.TickActorTransition() => _battlePresentationCoordinator.TickTransition();

    /// <inheritdoc/>
    void IBattleTickHost.ResolveOutcome() => ResolveBattleOutcome(BattleFlow);

    /// <inheritdoc/>
    void IBattleTickHost.MarkBattleResolved() => _battleResolved = true;

    /// <inheritdoc/>
    bool IBattleTickHost.AdvanceVictoryFade() => ++_battleReportFadeTicks >= BattleReportFadeFrames;

    /// <inheritdoc/>
    void IBattleTickHost.BeginFightReport()
    {
        _battleRenderer?.Hide();
        _fightReportController = BuildFightReportController(BattleFlow);

        // Draw the report's opening state before it becomes visible, so the frame after the victory fade
        // never shows the field behind it, or a previous battle's report.
        _fightReportRenderer?.Present(_fightReportController.View, BattleText(), BuildReportNames(), BuildReportItemNames());
        _fightReportRenderer?.Show();
        foreach (string log in _fightReportController.Logs)
        {
            _log.Warn(log, log);
        }
    }

    /// <inheritdoc/>
    void IBattleTickHost.EndBattle() => EndBattle();
}
