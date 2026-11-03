namespace Dw3Production.Simulation;

/// <summary>
/// Fixed-step clock that turns variable render frame times into 60 Hz simulation ticks, so
/// gameplay timings never drift with the display rate. Leftover time is carried to the next
/// frame. After a long stall the backlog is dropped instead of replayed, so the game never
/// runs in fast motion to catch up.
/// </summary>
public sealed class SimClock
{
    /// <summary>Length of one simulation tick in seconds, 1/60.</summary>
    public const double StepSeconds = 1.0 / 60.0;

    /// <summary>Most ticks run for one render frame. A frame owing more drops the excess.</summary>
    public const int MaxTicksPerFrame = 4;

    private double _debt;

    /// <summary>Gets the number of ticks dropped by the last <see cref="Tick"/>, 0 when the frame kept up. Logged as a hitch diagnostic.</summary>
    public int LastDroppedTicks { get; private set; }

    /// <summary>Adds a frame's elapsed time and returns how many simulation ticks to run now.</summary>
    /// <param name="deltaSeconds">Real time since the last frame, in seconds, 0 or more.</param>
    /// <returns>0..<see cref="MaxTicksPerFrame"/> ticks. When the cap is hit the carried time is reset to 0.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="deltaSeconds"/> is negative.</exception>
    public int Tick(double deltaSeconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(deltaSeconds);
        _debt += deltaSeconds;
        var requested = (int)Math.Floor((_debt + 1e-12) / StepSeconds);
        LastDroppedTicks = Math.Max(0, requested - MaxTicksPerFrame);
        var emitted = Math.Min(requested, MaxTicksPerFrame);
        _debt = LastDroppedTicks > 0 ? 0 : _debt - (emitted * StepSeconds);
        return emitted;
    }
}
