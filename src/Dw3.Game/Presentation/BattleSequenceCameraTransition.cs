namespace Dw3Production.Presentation;

/// <summary>Interpolates authored camera fields before composing their world transform.
/// Progress uses a 4096-unit fixed-point fraction and integer source ticks, not world-endpoint lerp.</summary>
internal sealed class BattleSequenceCameraTransition
{
    private readonly BattleSourceClock _clock = new();
    private SourceBattleCameraView _from = new();
    private SourceBattleCameraView _to = new();
    private int _progress = 4096;
    private int _step;

    /// <summary>Gets the independently owned interpolated source view.</summary>
    public SourceBattleCameraView Current { get; private set; } = new() { Projection = 280 };

    /// <summary>Seeds a view owned by the ordinary menu or overview camera.</summary>
    /// <param name="view">Last physical source view.</param>
    public void Seed(SourceBattleCameraView view)
    {
        Current = Blend(view, view, 0);
        _progress = 4096;
        _clock.Reset();
    }

    /// <summary>Starts a cut or fade from the currently displayed authored frame.</summary>
    /// <param name="view">Destination source view.</param>
    /// <param name="time">Duration in normalized source units, zero for a cut.</param>
    public void Begin(SourceBattleCameraView view, int time)
    {
        _from = Blend(Current, Current, 0);
        _to = Blend(view, view, 0);
        _progress = time == 0 ? 4096 : 0;
        _step = time == 0 ? 0 : 4096 / time;
        Current = Blend(_from, _to, _progress);
    }

    /// <summary>Advances normalized source units carried across presentation frames.</summary>
    /// <param name="delta">Elapsed presentation seconds.</param>
    public void Step(float delta)
    {
        _progress = Math.Min(4096, _progress + (_clock.Advance(delta) * _step));
        Current = Blend(_from, _to, _progress);
    }

    /// <summary>Blends source fields without interpolating an already composed rotation.</summary>
    /// <param name="from">Start frame.</param>
    /// <param name="to">Destination frame.</param>
    /// <param name="fraction">Fixed-point progress.</param>
    /// <returns>A new source frame with no mutable alias.</returns>
    private static SourceBattleCameraView Blend(SourceBattleCameraView from, SourceBattleCameraView to, int fraction) => new()
    {
        Vpx = Lerp(from.Vpx, to.Vpx, fraction),
        Vpy = Lerp(from.Vpy, to.Vpy, fraction),
        Vpz = Lerp(from.Vpz, to.Vpz, fraction),
        Vrx = Lerp(from.Vrx, to.Vrx, fraction),
        Vry = Lerp(from.Vry, to.Vry, fraction),
        Vrz = Lerp(from.Vrz, to.Vrz, fraction),
        Tx = Lerp(from.Tx, to.Tx, fraction),
        Ty = Lerp(from.Ty, to.Ty, fraction),
        Tz = Lerp(from.Tz, to.Tz, fraction),
        RotationX = Lerp(from.RotationX, to.RotationX, fraction),
        RotationY = Lerp(from.RotationY, to.RotationY, fraction),
        RotationZ = Lerp(from.RotationZ, to.RotationZ, fraction),
        Rz = Lerp(from.Rz, to.Rz, fraction),
        Projection = Lerp(from.Projection, to.Projection, fraction),
    };

    /// <summary>Interpolates one signed field in fixed point without shortest-arc remapping.</summary>
    /// <param name="from">Start field.</param>
    /// <param name="to">Destination field.</param>
    /// <param name="fraction">4096-unit blend fraction.</param>
    /// <returns>Interpolated integer field.</returns>
    private static int Lerp(int from, int to, int fraction) => from + (int)(((long)(to - from) * fraction) >> 12);
}
