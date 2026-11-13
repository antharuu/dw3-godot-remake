namespace Dw3Production.Simulation;

/// <summary>
/// The battle report's message line. A started message reveals one token every seven frames;
/// Cross reveals the rest at once, and once every token is shown the next Cross finishes it.
/// A finished message stays drawn until the report takes it with <see cref="TakeFinished"/>.
/// </summary>
/// <param name="tokenCounts">Token count per message entry; an entry missing here counts as one token.</param>
/// <param name="tokenCounter">Typed-glyph count of the message resolved with its own substitutions; when present it replaces <paramref name="tokenCounts"/>.</param>
internal sealed class FightReportMessageBox(IReadOnlyDictionary<int, int>? tokenCounts, Func<int, IReadOnlyList<int>, int>? tokenCounter = null)
{
    private bool _shown;
    private int _entry;
    private IReadOnlyList<int> _substitutions = [];
    private int _tokenCount;
    private int _revealed;
    private int _revealTimer;
    private bool _waiting;
    private bool _finished;

    /// <summary>Gets a value indicating whether no message is drawn.</summary>
    public bool IsEmpty => !_shown;

    /// <summary>Replaces the current message with message <paramref name="entry"/>, nothing revealed yet.</summary>
    /// <param name="entry">The message entry in the battle-report text.</param>
    /// <param name="substitutions">The numbers substituted into the message, in order.</param>
    public void Start(int entry, params int[] substitutions)
    {
        var count = tokenCounter?.Invoke(entry, substitutions) ?? ConfiguredCount(entry);
        _shown = true;
        _entry = entry;
        _substitutions = substitutions;
        _tokenCount = Math.Max(1, count);
        _revealed = 0;
        _revealTimer = 0;
        _waiting = false;
        _finished = false;
    }

    /// <summary>Advances the reveal one frame and handles one Cross press edge.</summary>
    /// <param name="crossPressedEdge">Whether Cross went down this frame.</param>
    public void Tick(bool crossPressedEdge)
    {
        if (!_shown || _finished)
        {
            return;
        }

        if (_waiting)
        {
            _finished = crossPressedEdge;
            return;
        }

        if (crossPressedEdge)
        {
            _revealed = _tokenCount;
            _waiting = true;
            return;
        }

        if (++_revealTimer > 6)
        {
            _revealTimer = 0;
            _revealed++;
            _waiting = _revealed >= _tokenCount;
        }
    }

    /// <summary>Clears the message when the player has finished reading it.</summary>
    /// <returns><see langword="true"/> when a finished message was cleared.</returns>
    public bool TakeFinished()
    {
        if (!_shown || !_finished)
        {
            return false;
        }

        _shown = false;
        return true;
    }

    /// <summary>The message as drawn this frame; the continue arrow cycles through five frames, one every five frames.</summary>
    /// <param name="time">The report's frame counter.</param>
    /// <returns>The message view, or <see langword="null"/> when no message is drawn.</returns>
    public FightReportMessage? ToView(int time) => _shown
        ? new FightReportMessage(_entry, _substitutions, _revealed, _tokenCount, _waiting && !_finished, (time / 5) % 5)
        : null;

    /// <summary>Reads the fixed token count configured for an entry when no resolved-text counter exists.</summary>
    /// <param name="entry">The message entry in the battle-report text.</param>
    /// <returns>The configured count, or one token.</returns>
    private int ConfiguredCount(int entry) => tokenCounts is not null && tokenCounts.TryGetValue(entry, out var configured) ? configured : 1;
}
