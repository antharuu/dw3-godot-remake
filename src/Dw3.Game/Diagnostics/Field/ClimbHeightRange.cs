namespace Dw3Production.Diagnostics.Field;

/// <summary>Lowest and highest climb heights seen over the ticks of a scripted climb.</summary>
internal sealed class ClimbHeightRange
{
    /// <summary>Gets the highest height seen, in 1/256 world units; <see cref="int.MinValue"/> before any tick.</summary>
    public int Max { get; private set; } = int.MinValue;

    /// <summary>Gets the lowest height seen, in 1/256 world units; <see cref="int.MaxValue"/> before any tick.</summary>
    public int Min { get; private set; } = int.MaxValue;

    /// <summary>Widens the range to include one tick height.</summary>
    /// <param name="height">Climb height after the tick.</param>
    public void Observe(int height)
    {
        Max = Math.Max(Max, height);
        Min = Math.Min(Min, height);
    }
}
