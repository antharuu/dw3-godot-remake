namespace Dw3Production.Simulation;

/// <summary>Coordinates command, enemy, terminal and report phases for one fixed battle tick.</summary>
public sealed class BattleTickCoordinator
{
    private readonly IBattleTickHost _host;

    /// <summary>Initializes a new instance of the <see cref="BattleTickCoordinator"/> class.</summary>
    /// <param name="host">Battle state and host-owned presentation operations.</param>
    public BattleTickCoordinator(IBattleTickHost host)
    {
        _host = host;
    }

    /// <summary>Advances one fixed battle tick.</summary>
    public void Tick()
    {
        BattleFlowController? flow = _host.Flow;
        if (flow is null)
        {
            _host.EndBattle();
            return;
        }

        if (_host.HasFightReport)
        {
            _host.TickFightReport();
            return;
        }

        TickActivePhase(flow);
        _host.RefreshModels();
        if (flow.Phase == BattleFlowPhase.Ended)
        {
            TickEnded(flow);
        }
    }

    /// <summary>Advances player-command or enemy-turn state.</summary>
    /// <param name="flow">Active battle flow.</param>
    private void TickActivePhase(BattleFlowController flow)
    {
        if (flow.Phase == BattleFlowPhase.PlayerCommand)
        {
            if (!_host.IsPlayerCameraActive)
            {
                _host.ActivatePlayerCamera();
            }

            _host.TickBattleMenu();
            if (flow.Phase != BattleFlowPhase.PlayerCommand)
            {
                _host.DeactivatePlayerCamera();
            }
        }
        else if (flow.Phase == BattleFlowPhase.EnemyTurn)
        {
            _host.DeactivatePlayerCamera();
            _host.EnemyAct();
        }
    }

    /// <summary>Applies a terminal outcome and starts the victory report after its fade.</summary>
    /// <param name="flow">Terminal battle flow.</param>
    private void TickEnded(BattleFlowController flow)
    {
        if (!_host.IsBattleResolved)
        {
            _host.ResolveOutcome();
            _host.MarkBattleResolved();
        }

        if (flow.Outcome != BattleOutcome.Won)
        {
            _host.EndBattle();
        }
        else if (_host.AdvanceVictoryFade())
        {
            _host.BeginFightReport();
        }
    }
}
