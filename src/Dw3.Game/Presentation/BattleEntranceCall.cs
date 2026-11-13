using Dw3Production.Simulation;

namespace Dw3Production.Presentation;

/// <summary>
/// An entrance as its source caller runs it: an optional message before it (a partner called back, an enemy
/// switching out) that waits for Cross, the entrance itself, then the message naming the incoming fighter, also closed
/// by Cross. Most source callers only go on once the entrance task is gone, so the post message starts with the
/// final camera fade (60 units on the player's side, 1 on the enemy's) and the battle waits for its Cross. The enemy
/// turn's voluntary switch instead opens its name message as soon as the entrance reports the swap (its
/// <c>done</c> flag, under full white), so that message runs alongside the flash fall, the hold and the return.
/// The call ends once both the entrance and the post message are over, so the next turn cannot take the camera
/// from a running entrance.
/// </summary>
internal sealed class BattleEntranceCall
{
    private readonly BattleEntranceMessage? _before;
    private readonly BattleEntranceMessage _after;
    private readonly Action _sequenceStarting;
    private readonly bool _afterAtSwap;
    private BattleMessageReveal? _reveal;
    private bool _afterOpened;

    /// <summary>Initializes a new instance of the <see cref="BattleEntranceCall"/> class.</summary>
    /// <param name="sequence">Entrance played between the messages.</param>
    /// <param name="before">Message closed before the entrance, or <see langword="null"/>.</param>
    /// <param name="after">Message naming the incoming fighter.</param>
    /// <param name="sequenceStarting">Runs once right before the entrance's first frame, to take the camera.</param>
    /// <param name="afterAtSwap">Whether the post message opens at the swap rather than after the entrance.</param>
    public BattleEntranceCall(BattleEntranceSequence sequence, BattleEntranceMessage? before, BattleEntranceMessage after, Action sequenceStarting, bool afterAtSwap = false)
    {
        ArgumentNullException.ThrowIfNull(sequence);
        ArgumentNullException.ThrowIfNull(after);
        ArgumentNullException.ThrowIfNull(sequenceStarting);
        Sequence = sequence;
        _before = before;
        _after = after;
        _sequenceStarting = sequenceStarting;
        _afterAtSwap = afterAtSwap;
        if (before is null)
        {
            StartSequence();
        }
        else
        {
            _reveal = new BattleMessageReveal(before.Lines);
        }
    }

    /// <summary>Gets the entrance played by the call.</summary>
    public BattleEntranceSequence Sequence { get; }

    /// <summary>Gets the current stage of the call.</summary>
    public BattleEntranceCallPhase Phase { get; private set; } = BattleEntranceCallPhase.Before;

    /// <summary>Gets the number of time units the post message has been open, zero before it.</summary>
    public int AfterFrames { get; private set; }

    /// <summary>Gets the open message box, or <see langword="null"/> while the entrance plays or after the call.</summary>
    public (BattleEntranceMessage Message, BattleMessageReveal Reveal)? OpenMessage => Phase switch
    {
        BattleEntranceCallPhase.Before when _before is not null && _reveal is not null => (_before, _reveal),
        BattleEntranceCallPhase.After when _reveal is not null => (_after, _reveal),
        BattleEntranceCallPhase.Sequence when _afterOpened && _reveal is { IsClosed: false } => (_after, _reveal),
        _ => null,
    };

    /// <summary>Plays one source time unit.</summary>
    /// <param name="confirm">Whether Cross was pressed in this unit, or <see langword="null"/> to close a message as
    /// soon as its arrow shows (scripted drivers).</param>
    public void Step(bool? confirm)
    {
        switch (Phase)
        {
            case BattleEntranceCallPhase.Before:
                if (StepMessage(confirm))
                {
                    StartSequence();
                }

                break;
            case BattleEntranceCallPhase.Sequence:
                StepSequence(confirm);
                break;
            case BattleEntranceCallPhase.After:
                AfterFrames++;
                if (StepMessage(confirm))
                {
                    Phase = BattleEntranceCallPhase.Done;
                }

                break;
            default:
                break;
        }
    }

    /// <summary>Starts the entrance and gives the camera to it.</summary>
    private void StartSequence()
    {
        _reveal = null;
        Phase = BattleEntranceCallPhase.Sequence;
        _sequenceStarting();
    }

    /// <summary>Plays one entrance frame, steps a post message the swap already opened, and opens the post message
    /// at the swap or once the entrance task would be gone, as the caller does.</summary>
    /// <param name="confirm">Cross this unit, or <see langword="null"/> to confirm on the arrow.</param>
    private void StepSequence(bool? confirm)
    {
        if (_afterOpened && _reveal is { IsClosed: false })
        {
            AfterFrames++;
            StepMessage(confirm);
        }

        bool messageClosed = _afterOpened && _reveal is { IsClosed: true };
        Sequence.Step();
        if (!_afterOpened && (Sequence.IsComplete || (_afterAtSwap && Sequence.Swapped)))
        {
            _afterOpened = true;
            _reveal = new BattleMessageReveal(_after.Lines);
        }

        if (Sequence.IsComplete)
        {
            Phase = messageClosed ? BattleEntranceCallPhase.Done : BattleEntranceCallPhase.After;
        }
    }

    /// <summary>Steps the open message box.</summary>
    /// <param name="confirm">Cross this unit, or <see langword="null"/> to confirm on the arrow.</param>
    /// <returns>Whether the box closed.</returns>
    private bool StepMessage(bool? confirm)
    {
        if (_reveal is not { } reveal)
        {
            return true;
        }

        reveal.Step(confirm ?? reveal.Arrow);
        return reveal.IsClosed;
    }
}
