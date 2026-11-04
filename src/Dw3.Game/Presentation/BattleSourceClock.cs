namespace Dw3Production.Presentation;

/// <summary>
/// Converts presentation elapsed time into the original game's 60 Hz timing units. The PAL
/// executable exposes these units through <c>GFX.funcs.getFrameTime()</c>, including one or two
/// ticks per PAL-presented frame.
/// </summary>
internal sealed class BattleSourceClock
{
    private const float SourceTicksPerSecond = 60f;

    private float _accumulated;

    /// <summary>Adds presented time and returns the complete 60 Hz source ticks it covers.</summary>
    /// <param name="delta">Elapsed presentation time in seconds.</param>
    /// <returns>The number of complete source ticks, possibly zero.</returns>
    public int Advance(float delta)
    {
        _accumulated += delta * SourceTicksPerSecond;
        var ticks = (int)_accumulated;
        _accumulated -= ticks;
        return ticks;
    }

    /// <summary>Discards any fractional source-tick remainder.</summary>
    public void Reset() => _accumulated = 0f;
}
