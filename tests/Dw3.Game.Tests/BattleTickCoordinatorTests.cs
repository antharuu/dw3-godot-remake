using Dw3Production.Simulation;
using Xunit;

namespace Dw3.Game.Tests;

public sealed class BattleTickCoordinatorTests
{
    [Fact]
    public void TickDeactivatesTheCommandCameraWhenACommandClosesTheTurn()
    {
        var host = new CommandClosingHost();
        var coordinator = new BattleTickCoordinator(host);

        coordinator.Tick();

        Assert.Equal(1, host.ActivateCameraCalls);
        Assert.Equal(1, host.DeactivateCameraCalls);
    }

    [Fact]
    public void Tick_ActorTransitionHoldsTheFlowUntilItEnds()
    {
        var host = new CommandClosingHost { TransitionFrames = 2 };
        var coordinator = new BattleTickCoordinator(host);

        coordinator.Tick();
        coordinator.Tick();
        Assert.Equal(0, host.ActivateCameraCalls);

        coordinator.Tick();
        Assert.Equal(1, host.ActivateCameraCalls);
    }

    /// <summary>Applies terminal victory effects once even while the report fade takes several ticks.</summary>
    [Fact]
    public void Tick_TerminalVictoryResolvesOutcomeOnlyOnce()
    {
        var host = new TerminalOutcomeHost();
        var coordinator = new BattleTickCoordinator(host);

        coordinator.Tick();
        coordinator.Tick();

        Assert.Equal(1, host.ResolveOutcomeCalls);
        Assert.True(host.IsBattleResolved);
    }

    private sealed class CommandClosingHost : IBattleTickHost
    {
        public CommandClosingHost()
        {
            Flow = new BattleFlowController();
            Flow.Start(new BattleState());
        }

        public int ActivateCameraCalls { get; private set; }

        public int DeactivateCameraCalls { get; private set; }

        public int TransitionFrames { get; set; }

        public BattleFlowController? Flow { get; }

        public bool HasFightReport => false;

        public bool IsPlayerCameraActive => ActivateCameraCalls > DeactivateCameraCalls;

        public bool IsBattleResolved => false;

        public void ActivatePlayerCamera() => ActivateCameraCalls++;

        public void BeginFightReport()
        {
        }

        public void DeactivatePlayerCamera() => DeactivateCameraCalls++;

        public void EndBattle()
        {
        }

        public void EnemyAct()
        {
        }

        public void TickActionSequence()
        {
        }

        public void MarkBattleResolved()
        {
        }

        public void RefreshModels()
        {
        }

        public bool TickActorTransition() => TransitionFrames-- > 0;

        public void ResolveOutcome()
        {
        }

        public void TickBattleMenu() => Flow!.EndBattle(BattleOutcome.Won);

        public void TickFightReport()
        {
        }

        public bool AdvanceVictoryFade() => false;
    }

    /// <summary>Represents a terminal victory whose report has not yet opened.</summary>
    private sealed class TerminalOutcomeHost : IBattleTickHost
    {
        public TerminalOutcomeHost()
        {
            Flow = new BattleFlowController();
            Flow.Start(new BattleState());
            Flow.EndBattle(BattleOutcome.Won);
        }

        public BattleFlowController? Flow { get; }

        public bool HasFightReport => false;

        public bool IsPlayerCameraActive => false;

        public bool IsBattleResolved { get; private set; }

        public int ResolveOutcomeCalls { get; private set; }

        public void ActivatePlayerCamera()
        {
        }

        public bool AdvanceVictoryFade() => false;

        public void BeginFightReport()
        {
        }

        public void DeactivatePlayerCamera()
        {
        }

        public void EndBattle()
        {
        }

        public void EnemyAct()
        {
        }

        public void MarkBattleResolved() => IsBattleResolved = true;

        public void RefreshModels()
        {
        }

        public bool TickActorTransition() => false;

        public void ResolveOutcome() => ResolveOutcomeCalls++;

        public void TickActionSequence()
        {
        }

        public void TickBattleMenu()
        {
        }

        public void TickFightReport()
        {
        }
    }
}
