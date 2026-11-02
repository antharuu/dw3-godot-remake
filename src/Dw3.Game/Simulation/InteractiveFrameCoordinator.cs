namespace Dw3Production.Simulation;

/// <summary>Coordinates one interactive rendered frame around fixed simulation and single final presentation.</summary>
public sealed class InteractiveFrameCoordinator
{
    private readonly SimClock _clock;

    /// <summary>Initializes the coordinator with the session's fixed-step clock.</summary>
    /// <param name="clock">Clock that converts rendered delta time into fixed ticks.</param>
    public InteractiveFrameCoordinator(SimClock clock)
    {
        _clock = clock;
    }

    /// <summary>Advances the interactive session once, preserving focus, title, fixed-step and single-presentation gates.</summary>
    /// <param name="host">Frame state and simulation/presentation operations.</param>
    /// <param name="delta">Elapsed rendered-frame time.</param>
    public void Advance(IInteractiveFrameHost host, double delta)
    {
        if (host.IsTestDriverActive || !host.HasInputFocus)
        {
            return;
        }

        if (host.IsInTitle)
        {
            host.ShowTitle();
            return;
        }

        host.StartDebugBattleIfRequested();
        if (!host.HasField)
        {
            return;
        }

        int steps = _clock.Tick(delta);
        LogDroppedTicks(host);
        for (var step = 0; step < steps; step++)
        {
            host.SimulateStep();
        }

        Present(host, delta);
    }

    /// <summary>Writes the established stall warning only when the fixed-step cap dropped ticks.</summary>
    /// <param name="host">Frame host that receives the warning.</param>
    private void LogDroppedTicks(IInteractiveFrameHost host)
    {
        if (_clock.LastDroppedTicks > 0)
        {
            host.LogSimulationStall(_clock.LastDroppedTicks, SimClock.MaxTicksPerFrame);
        }
    }

    /// <summary>Presents exactly one final state after all fixed steps have completed.</summary>
    /// <param name="host">Frame host that provides the active presentation mode.</param>
    /// <param name="delta">Elapsed rendered-frame time.</param>
    private static void Present(IInteractiveFrameHost host, double delta)
    {
        if (host.HasActiveBattle)
        {
            host.PresentBattle();
            return;
        }

        host.PresentField(delta);
    }
}
