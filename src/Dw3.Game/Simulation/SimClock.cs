namespace Dw3Production.Simulation;

public sealed class SimClock
{
    public const double StepSeconds = 1.0 / 60.0;
    public const int MaxTicksPerFrame = 4;
    private double _debt;

    public int LastDroppedTicks { get; private set; }

    public int Tick(double deltaSeconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(deltaSeconds);
        _debt += deltaSeconds;
        var requested = (int)Math.Floor((_debt + 1e-12) / StepSeconds);
        LastDroppedTicks = Math.Max(0, requested - MaxTicksPerFrame);
        var emitted = Math.Min(requested, MaxTicksPerFrame);
        _debt = LastDroppedTicks > 0 ? 0 : _debt - emitted * StepSeconds;
        return emitted;
    }
}
