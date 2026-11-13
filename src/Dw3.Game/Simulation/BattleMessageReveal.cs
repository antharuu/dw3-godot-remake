namespace Dw3Production.Simulation;

/// <summary>
/// The source battle message box's line reveal: one line shown, then the next after more than three frames,
/// and once every line is out the blinking arrow, after which Cross closes the message. A confirmation is only
/// read once the arrow is shown, as the source polls the pad's new presses in that state alone.
/// </summary>
public sealed class BattleMessageReveal
{
    /// <summary>The source waits for more than this many frames between two lines.</summary>
    public const int LineInterval = 3;

    private readonly int _lineCount;
    private int _state;
    private int _time;
    private int _shownTime;
    private int _shown;

    /// <summary>Initializes a new instance of the <see cref="BattleMessageReveal"/> class.</summary>
    /// <param name="lineCount">Number of message lines, one or two.</param>
    /// <exception cref="ArgumentOutOfRangeException">The message has no line or more than two.</exception>
    public BattleMessageReveal(int lineCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(lineCount, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(lineCount, 2);
        _lineCount = lineCount;
    }

    /// <summary>Gets the number of lines currently visible.</summary>
    public int VisibleLines { get; private set; }

    /// <summary>Gets a value indicating whether the continuation arrow is shown, the only state that reads Cross.</summary>
    public bool Arrow => _state == 3;

    /// <summary>Gets a value indicating whether Cross closed the message.</summary>
    public bool IsClosed { get; private set; }

    /// <summary>Advances one source frame.</summary>
    /// <param name="confirmPressed">Whether Cross was newly pressed this frame.</param>
    public void Step(bool confirmPressed)
    {
        _time++;
        switch (_state)
        {
            case 0:
                _state = 1;
                break;
            case 1:
                _shownTime = _time;
                VisibleLines = _shown + 1;
                _state = 2;
                break;
            case 2:
                AdvanceLine();
                break;
            default:
                IsClosed |= confirmPressed;
                break;
        }
    }

    /// <summary>Shows the next line or the arrow once more than <see cref="LineInterval"/> frames passed.</summary>
    private void AdvanceLine()
    {
        if (_time - _shownTime <= LineInterval)
        {
            return;
        }

        _shown++;
        _state = _shown >= _lineCount ? 3 : 1;
    }
}
