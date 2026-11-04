namespace Dw3Production.Simulation;

/// <summary>Coordinates one interactive rendered frame around fixed simulation and single final presentation.</summary>
public sealed class InteractiveFrameCoordinator
{
    /// <summary>
    /// Number of frames to suppress stall warnings after a loading boundary.
    /// One frame covers the first long delta that arrives right after a scene load, warp, or battle entry.
    /// </summary>
    private const int StallGraceFrames = 1;

    private readonly SimClock _clock;
    private int _graceFramesRemaining;

    /// <summary>Initializes a new instance of the <see cref="InteractiveFrameCoordinator"/> class.</summary>
    /// <param name="clock">Clock that converts rendered delta time into fixed ticks.</param>
    public InteractiveFrameCoordinator(SimClock clock)
    {
        _clock = clock;
    }

    /// <summary>
    /// Marks the start of a loading boundary (scene load, warp, or battle entry).
    /// Dropped ticks in the next <see cref="StallGraceFrames"/> rendered frames are not reported
    /// as stalls because the first frame after a load always carries extra accumulated time.
    /// </summary>
    public void NotifyLoadingStart() => _graceFramesRemaining = StallGraceFrames;

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

        if (host.HasActiveBattle)
        {
            host.PresentBattle();
            return;
        }

        host.PresentField(delta);
    }

    /// <summary>
    /// Writes the stall warning only when the fixed-step cap dropped ticks and no grace window is active.
    /// The grace window is consumed for one frame after a loading boundary.
    /// </summary>
    /// <param name="host">Frame host that receives the warning.</param>
    private void LogDroppedTicks(IInteractiveFrameHost host)
    {
        if (_graceFramesRemaining > 0)
        {
            _graceFramesRemaining--;
            return;
        }

        if (_clock.LastDroppedTicks > 0)
        {
            host.LogSimulationStall(_clock.LastDroppedTicks, SimClock.MaxTicksPerFrame);
        }
    }
}
