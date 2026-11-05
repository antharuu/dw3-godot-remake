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

    private sealed class CommandClosingHost : IBattleTickHost
    {
        public CommandClosingHost()
        {
            Flow = new BattleFlowController();
            Flow.Start(new BattleState());
        }

        public int ActivateCameraCalls { get; private set; }

        public int DeactivateCameraCalls { get; private set; }

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

        public void MarkBattleResolved()
        {
        }

        public void RefreshModels()
        {
        }

        public void ResolveOutcome()
        {
        }

        public void TickBattleMenu() => Flow!.EndBattle(BattleOutcome.Won);

        public void TickFightReport()
        {
        }

        public bool AdvanceVictoryFade() => false;
    }
}
