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
    void IBattleTickHost.ResolveOutcome() => ResolveBattleOutcome(BattleFlow);

    /// <inheritdoc/>
    void IBattleTickHost.MarkBattleResolved() => _battleResolved = true;

    /// <inheritdoc/>
    bool IBattleTickHost.AdvanceVictoryFade() => ++_battleReportFadeTicks >= BattleReportFadeFrames;

    /// <inheritdoc/>
    void IBattleTickHost.BeginFightReport()
    {
        _battleRenderer?.Hide();
        _fightReportRenderer?.Show();
        _fightReportController = BuildFightReportController(BattleFlow);
        foreach (string log in _fightReportController.Logs)
        {
            _log.Warn(log, log);
        }
    }

    /// <inheritdoc/>
    void IBattleTickHost.EndBattle() => EndBattle();
}
