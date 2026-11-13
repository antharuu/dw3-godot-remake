using Dw3Production.Conversion;
using Dw3Production.Simulation;

namespace Dw3Production.Presentation;

/// <summary>
/// Consumes the battle-script sprite-effect and screen-fade commands from the prepared sprite domain:
/// load and start, the source limit of eight effects per script, one source sample per physical step,
/// the additive white flash and the removal of every child on completion or cancellation.
/// </summary>
internal sealed class BattleSpriteEffectPlayback : IDisposable
{
    /// <summary>The source script keeps at most this many running sprite effects; another start is dropped.</summary>
    public const int MaxScriptEffects = 8;

    private readonly Func<BattleSpriteCatalog> _catalog;
    private readonly IBattleSpriteSurface _surface;
    private readonly List<BattleSpriteEffectInstance> _active = [];
    private readonly List<BattleWhiteFlash> _flashes = [];
    private BattleWhiteFlash? _currentFlash;
    private long _serial;

    /// <summary>Initializes a new instance of the <see cref="BattleSpriteEffectPlayback"/> class.</summary>
    /// <param name="catalog">Lazy validated sprite domain, read from the prepared pack only.</param>
    /// <param name="surface">Drawing side.</param>
    public BattleSpriteEffectPlayback(Func<BattleSpriteCatalog> catalog, IBattleSpriteSurface surface)
    {
        _catalog = catalog;
        _surface = surface;
    }

    /// <summary>Gets the drawing side, for private lifecycle diagnostics.</summary>
    public IBattleSpriteSurface Surface => _surface;

    /// <summary>Gets the running effects in creation order.</summary>
    public IReadOnlyList<BattleSpriteEffectInstance> Active => _active;

    /// <summary>Gets the live white flashes, the latest last.</summary>
    public IReadOnlyList<BattleWhiteFlash> Flashes => _flashes;

    /// <summary>Gets the number of effects started since the last cleanup, identical effects counted separately.</summary>
    public int StartedCount { get; private set; }

    /// <summary>Gets the summed additive white level currently drawn.</summary>
    public int FlashLevel => Math.Min(BattleWhiteFlash.FullLevel, _flashes.Sum(flash => flash.Level));

    /// <summary>Gets a value indicating whether every sprite child ended. The source script never waits for its fade.</summary>
    public bool IsComplete => _active.Count == 0;

    /// <summary>Consumes a load (mode 1) or start (mode 0) sprite command.</summary>
    /// <param name="request">Validated request, its dynamic identity already resolved.</param>
    /// <returns>Applied for a drawn or source-qualified effect, unavailable for a missing or unsupported identity.</returns>
    /// <exception cref="CacheResourceException">The prepared sprite domain is unavailable or corrupt.</exception>
    public SequenceAcknowledgement Consume(SpriteEffectEvent request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var a = request.Arguments;
        BattleSpriteResolution resolution = _catalog().Resolve(a[1]);
        switch (resolution.Availability)
        {
            case BattleSpriteAvailability.SourceAbsent:
                return SequenceAcknowledgement.Applied;
            case BattleSpriteAvailability.Available when a[0] == 1:
                return SequenceAcknowledgement.Applied;
            case BattleSpriteAvailability.Available when a.Count == 5:
                Start(resolution.Sheet!, new BattleSpriteAnchor(a[2], a[3], a[4]));
                return SequenceAcknowledgement.Applied;
            default:
                return SequenceAcknowledgement.Unavailable;
        }
    }

    /// <summary>Consumes a screen-fade command: 0 starts a white flash, 1 ends the latest one.</summary>
    /// <param name="request">Source fade mode and duration.</param>
    /// <returns>Applied, or unavailable for a duration the source cannot divide by.</returns>
    public SequenceAcknowledgement Consume(ScreenFadeEvent request)
    {
        ArgumentNullException.ThrowIfNull(request);
        int frames = request.Arguments[1];
        if (frames <= 0)
        {
            return SequenceAcknowledgement.Unavailable;
        }

        if (request.Arguments[0] == 0)
        {
            _currentFlash = new BattleWhiteFlash(frames);
            _flashes.Add(_currentFlash);
        }
        else
        {
            _currentFlash?.End(frames);
        }

        return SequenceAcknowledgement.Applied;
    }

    /// <summary>Advances every child one source frame, removes the finished ones and redraws.</summary>
    public void Step()
    {
        foreach (BattleSpriteEffectInstance instance in _active)
        {
            instance.Step();
        }

        foreach (BattleSpriteEffectInstance done in _active.Where(instance => instance.IsComplete).ToArray())
        {
            _active.Remove(done);
            _surface.Detach(done);
        }

        foreach (BattleWhiteFlash flash in _flashes)
        {
            flash.Step();
        }

        _flashes.RemoveAll(flash => flash.Phase == BattleWhiteFlashPhase.Ended);
        _surface.Present(_active, FlashLevel);
    }

    /// <summary>Destroys every child after normal completion or cancellation.</summary>
    public void Clear()
    {
        foreach (BattleSpriteEffectInstance instance in _active)
        {
            _surface.Detach(instance);
        }

        _active.Clear();
        _flashes.Clear();
        _currentFlash = null;
        StartedCount = 0;
        _surface.Present(_active, 0);
    }

    /// <summary>Releases the drawing side.</summary>
    public void Dispose()
    {
        Clear();
        (_surface as IDisposable)?.Dispose();
    }

    /// <summary>Starts one independent instance unless the script already runs eight effects.</summary>
    /// <param name="sheet">Prepared sheet.</param>
    /// <param name="anchor">Script position.</param>
    private void Start(BattleSpriteSheet sheet, BattleSpriteAnchor anchor)
    {
        if (_active.Count >= MaxScriptEffects)
        {
            return;
        }

        var instance = new BattleSpriteEffectInstance(++_serial, sheet, anchor);
        _active.Add(instance);
        StartedCount++;
        _surface.Attach(instance);
        _surface.Present(_active, FlashLevel);
    }
}
