namespace Dw3Production.Presentation;

/// <summary>
/// Converts presented frame time to whole PAL (50 Hz) source frames without drift: the fractional
/// remainder is carried to the next call.
/// </summary>
internal sealed class BattlePalClock
{
    private const float PalFramesPerSecond = 50f;

    private float _accumulated;

    /// <summary>Adds presented time and takes the whole PAL frames it completes.</summary>
    /// <param name="delta">Presented time in seconds.</param>
    /// <returns>The number of whole PAL frames elapsed, possibly zero.</returns>
    public int Advance(float delta)
    {
        _accumulated += delta * PalFramesPerSecond;
        var frames = (int)_accumulated;
        _accumulated -= frames;
        return frames;
    }

    /// <summary>Drops the carried fraction.</summary>
    public void Reset() => _accumulated = 0f;
}
