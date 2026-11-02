using Dw3Production.Simulation;
using Xunit;

namespace Dw3.Game.Tests;

public sealed class InteractiveFrameCoordinatorTests
{
    [Fact]
    public void AdvancesFixedStepsThenPresentsTheFinalBattleState()
    {
        var host = new FrameHost { HasField = true, ActivateBattleOnTick = true };
        var coordinator = new InteractiveFrameCoordinator(new SimClock());

        coordinator.Advance(host, 1.0 / 60.0);

        Assert.Equal(1, host.SimulatedTicks);
        Assert.Equal(1, host.BattlePresents);
        Assert.Equal(0, host.FieldPresents);
    }

    [Fact]
    public void StopsBeforeSimulationWhenTheTestDriverOwnsInput()
    {
        var host = new FrameHost { HasField = true, IsTestDriverActive = true };
        var coordinator = new InteractiveFrameCoordinator(new SimClock());

        coordinator.Advance(host, 1.0 / 60.0);

        Assert.Equal(0, host.SimulatedTicks);
        Assert.Equal(0, host.FieldPresents + host.BattlePresents);
    }

    [Fact]
    public void KeepsTheTitleVisibleWithoutDrivingTheField()
    {
        var host = new FrameHost { HasField = true, IsInTitle = true };
        var coordinator = new InteractiveFrameCoordinator(new SimClock());

        coordinator.Advance(host, 1.0 / 60.0);

        Assert.Equal(1, host.TitleShows);
        Assert.Equal(0, host.SimulatedTicks);
    }

    private sealed class FrameHost : IInteractiveFrameHost
    {
        public bool ActivateBattleOnTick { get; init; }

        public int BattlePresents { get; private set; }

        public int FieldPresents { get; private set; }

        public bool HasActiveBattle { get; private set; }

        public bool HasField { get; init; }

        public bool HasInputFocus { get; init; } = true;

        public bool IsInTitle { get; init; }

        public bool IsTestDriverActive { get; init; }

        public int SimulatedTicks { get; private set; }

        public int TitleShows { get; private set; }

        public void LogSimulationStall(int droppedTicks, int cap)
        {
        }

        public void StartDebugBattleIfRequested()
        {
        }

        public void PresentBattle()
        {
            BattlePresents++;
        }

        public void PresentField(double delta)
        {
            FieldPresents++;
        }

        public void SimulateStep()
        {
            SimulatedTicks++;
            HasActiveBattle = ActivateBattleOnTick;
        }

        public void ShowTitle()
        {
            TitleShows++;
        }
    }
}
