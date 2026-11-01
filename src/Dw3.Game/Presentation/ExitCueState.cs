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

    public ExitCueState(IReadOnlyDictionary<int, IReadOnlyList<(int Frame, int Duration)>> innerRows)
    {
        _innerRows = innerRows;
    }

    public ExitCuePhase Phase { get; private set; } = ExitCuePhase.Hidden;

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

    public void Reset()
    {
        Phase = ExitCuePhase.Hidden;
        _sourceRemainder = 0;
        _pop = 0;
        _innerElapsed = 0;
        _rawDirection = 0;
    }

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

    private int NextSourceUnits()
    {
        _sourceRemainder += SourceIncrementNumerator;
        var units = _sourceRemainder / SourceIncrementDenominator;
        _sourceRemainder %= SourceIncrementDenominator;
        return units;
    }

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
