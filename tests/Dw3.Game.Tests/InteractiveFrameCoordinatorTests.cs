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

    // ── Stall grace window ──────────────────────────────────────────────────

    /// <summary>
    /// A very long frame immediately after a loading event must not trigger the stall warning.
    /// The first Advance after NotifyLoadingStart is in the grace window.
    /// </summary>
    [Fact]
    public void SuppressesStallOnFirstFrameAfterLoadingStart()
    {
        // Arrange: produce a delta large enough to drop ticks (> 4 × 1/60 s).
        const double longFrame = 10.0;
        var host = new FrameHost { HasField = true };
        var coordinator = new InteractiveFrameCoordinator(new SimClock());

        coordinator.NotifyLoadingStart();

        // Act: the first frame is in the grace window.
        coordinator.Advance(host, longFrame);

        // Assert: no stall warning emitted.
        Assert.Equal(0, host.StallCount);
    }

    /// <summary>
    /// A very long frame that is NOT preceded by a loading notification must trigger the stall warning.
    /// </summary>
    [Fact]
    public void ReportsStallForLongFrameInNormalPlay()
    {
        const double longFrame = 10.0;
        var host = new FrameHost { HasField = true };
        var coordinator = new InteractiveFrameCoordinator(new SimClock());

        // No NotifyLoadingStart — regular in-game frame.
        coordinator.Advance(host, longFrame);

        Assert.True(host.StallCount > 0, "expected at least one stall warning for a 10-second frame");
    }

    /// <summary>
    /// After the grace window expires (one suppressed frame), a subsequent long frame must
    /// trigger the stall warning again.
    /// </summary>
    [Fact]
    public void ReportsStallOnSecondLongFrameAfterLoadingStart()
    {
        const double longFrame = 10.0;
        var host = new FrameHost { HasField = true };
        var coordinator = new InteractiveFrameCoordinator(new SimClock());

        coordinator.NotifyLoadingStart();
        coordinator.Advance(host, longFrame);  // first frame: grace, no warning.
        coordinator.Advance(host, longFrame);  // second frame: normal, warning expected.

        Assert.True(host.StallCount > 0, "stall should be reported after the grace window expires");
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

        /// <summary>Gets the number of times LogSimulationStall was called.</summary>
        public int StallCount { get; private set; }

        public void LogSimulationStall(int droppedTicks, int cap) => StallCount++;

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
