namespace Dw3Production.Presentation;

/// <summary>
/// Source-time state for the field offer balloon. The EU source advances its
/// 8.8 frame clock by 0x133 at 50 PAL VSYNCs per second. M1 runs simulation at
/// 60 Hz, so every fixed step contributes 50 * 0x133 / (60 * 0x100) source units.
/// The pop sprite therefore runs 0xC8..0xD4 while opening and 0xD4..0xDC while
/// closing, exactly as the exit-cue timing contract does before selecting pop >> 2.
/// </summary>
public sealed class ExitCueState
{
    private const int OpeningStart = 0xC8;
    private const int OpenAt = 0xD4;
    private const int ClosingEnd = 0xDC;
    private const int SourceIncrementNumerator = 50 * 0x133;
    private const int SourceIncrementDenominator = 60 * 0x100;

    private readonly IReadOnlyDictionary<int, IReadOnlyList<(int Frame, int Duration)>> _innerRows;
    private int _sourceRemainder;
    private int _pop;
    private int _innerElapsed;
    private int _rawDirection;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExitCueState"/> class.
    /// </summary>
    /// <param name="innerRows">Per-direction row map from the field cue archive: key is the row id (6..10),
    /// value is the ordered (frame, duration) sequence used to animate the open cue sprite.</param>
    public ExitCueState(IReadOnlyDictionary<int, IReadOnlyList<(int Frame, int Duration)>> innerRows)
    {
        _innerRows = innerRows;
    }

    /// <summary>Gets the current visibility and transition phase of the field cue.</summary>
    public ExitCuePhase Phase { get; private set; } = ExitCuePhase.Hidden;

    /// <summary>Advances the cue by one simulation step and returns the frame selection for rendering.</summary>
    /// <param name="offered">Whether the current field position offers an exit or interaction cue.</param>
    /// <param name="rawDirection">Player-facing direction in the inclusive range 0 through 7.</param>
    /// <returns>A snapshot containing the cue phase, outer frame, optional inner frame, and facing direction.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="rawDirection"/> is outside 0 through 7.</exception>
    public ExitCueSnapshot Advance(bool offered, int rawDirection)
    {
        if (rawDirection is < 0 or > 7)
        {
            throw new ArgumentOutOfRangeException(nameof(rawDirection));
        }

        var sourceUnits = NextSourceUnits();
        if (offered)
        {
            AdvanceOffered(rawDirection, sourceUnits);
        }
        else
        {
            AdvanceNotOffered(sourceUnits);
        }

        if (Phase == ExitCuePhase.Hidden)
        {
            return new ExitCueSnapshot(Phase, 0, null, _rawDirection);
        }

        int? innerFrame = Phase == ExitCuePhase.Open ? CurrentInnerFrame() : null;
        return new ExitCueSnapshot(Phase, _pop >> 2, innerFrame, _rawDirection);
    }

    /// <summary>Clears all accumulated timing and returns the cue to its hidden initial state.</summary>
    public void Reset()
    {
        Phase = ExitCuePhase.Hidden;
        _sourceRemainder = 0;
        _pop = 0;
        _innerElapsed = 0;
        _rawDirection = 0;
    }

    /// <summary>Opens or advances the cue while an exit is available to the player.</summary>
    /// <param name="rawDirection">Current player-facing direction in the source eight-direction encoding.</param>
    /// <param name="sourceUnits">Elapsed source-clock units accumulated for this simulation step.</param>
    private void AdvanceOffered(int rawDirection, int sourceUnits)
    {
        _rawDirection = rawDirection;
        if (Phase is ExitCuePhase.Hidden or ExitCuePhase.Closing)
        {
            Phase = ExitCuePhase.Opening;
            _pop = OpeningStart;
            _innerElapsed = 0;
        }

        if (Phase == ExitCuePhase.Opening)
        {
            _pop = Math.Min(OpenAt, _pop + sourceUnits);
            if (_pop == OpenAt)
            {
                Phase = ExitCuePhase.Open;
            }
        }
        else if (Phase == ExitCuePhase.Open)
        {
            _innerElapsed = checked(_innerElapsed + sourceUnits);
        }
    }

    /// <summary>Closes a visible cue after the player no longer faces an offered exit.</summary>
    /// <param name="sourceUnits">Elapsed source-clock units accumulated for this simulation step.</param>
    private void AdvanceNotOffered(int sourceUnits)
    {
        if (Phase is ExitCuePhase.Opening or ExitCuePhase.Open)
        {
            Phase = ExitCuePhase.Closing;
            _pop = OpenAt;
        }
        else if (Phase == ExitCuePhase.Closing)
        {
            _pop = Math.Min(ClosingEnd, _pop + sourceUnits);
            if (_pop == ClosingEnd)
            {
                Phase = ExitCuePhase.Hidden;
            }
        }
    }

    /// <summary>Converts one 60 Hz simulation step into whole source-clock units while preserving fractions.</summary>
    /// <returns>The whole source-clock units available to this step.</returns>
    private int NextSourceUnits()
    {
        _sourceRemainder += SourceIncrementNumerator;
        var units = _sourceRemainder / SourceIncrementDenominator;
        _sourceRemainder %= SourceIncrementDenominator;
        return units;
    }

    /// <summary>Resolves the looping inner animation frame for the remembered player-facing direction.</summary>
    /// <returns>The selected frame identifier from the directional cue row.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the selected row is absent, has no positive duration, or cannot yield a frame.</exception>
    private int CurrentInnerFrame()
    {
        var row = _rawDirection == 4 ? 10 : (_rawDirection >> 1) + 6;
        if (!_innerRows.TryGetValue(row, out var sequence) || sequence.Count == 0)
        {
            throw new InvalidOperationException($"cue direction row absent={row}");
        }

        var total = sequence.Sum(item => item.Duration);
        if (total <= 0)
        {
            throw new InvalidOperationException($"cue direction row has invalid duration={row}");
        }

        var elapsed = _innerElapsed % total;
        foreach (var item in sequence)
        {
            if (elapsed < item.Duration)
            {
                return item.Frame;
            }

            elapsed -= item.Duration;
        }

        throw new InvalidOperationException("cue animation sequence exhausted unexpectedly");
    }
}
