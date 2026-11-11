using Dw3.Data.Battle;

namespace Dw3Production.Simulation;

/// <summary>Engine-free action-script timeline that advances one frame at a time.
/// The hit result is pre-resolved and frozen: no RNG is drawn during playback.
/// Wait opcodes are the only timing primitive; motion-end and other physical conditions
/// are expressed by downstream consumers (B9-052/054/055) through separate synchronization.</summary>
public sealed class BattleActionTimeline
{
    private readonly BattleActionScript _script;
    private readonly ActionSequenceContext _context;
    private int _cursor;
    private int _waitFrames;
    private bool _complete;

    /// <summary>Initializes a new instance of the <see cref="BattleActionTimeline"/> class.</summary>
    /// <param name="script">Decoded action script to play.</param>
    /// <param name="context">Frozen identity and pre-resolved hit events.</param>
    public BattleActionTimeline(BattleActionScript script, ActionSequenceContext context)
    {
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(context);
        _script = script;
        _context = context;
    }

    /// <summary>Gets the frozen identity and hit events for this sequence.</summary>
    public ActionSequenceContext Context => _context;

    /// <summary>Gets a value indicating whether the script reached its End opcode.</summary>
    public bool IsComplete => _complete;

    /// <summary>Gets the current script cursor position (zero-based command index).</summary>
    public int Cursor => _cursor;

    /// <summary>Gets the remaining wait-frame count. Zero when no Wait is active.</summary>
    public int WaitFrames => _waitFrames;

    /// <summary>Advances the timeline by the given number of frames and returns the events that fired.
    /// Returns an empty list when the script is already complete or the wait has not expired.
    /// A single call with <paramref name="frames"/> greater than 1 is deterministic: multi-frame advances
    /// are identical to advancing one frame at a time.</summary>
    /// <param name="frames">Number of frames to advance. Must be at least 1.</param>
    /// <returns>The typed events emitted during this advance, in script order.</returns>
    public IReadOnlyList<BattleSequenceEvent> Advance(int frames = 1)
    {
        if (frames < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(frames), frames, "Frames must be at least 1.");
        }

        if (_complete)
        {
            return [];
        }

        var events = new List<BattleSequenceEvent>();
        for (int f = 0; f < frames && !_complete; f++)
        {
            AdvanceOneFrame(events);
        }

        return events;
    }

    /// <summary>Marks the sequence as complete immediately without emitting further events.
    /// Used when a KO or interruption cuts the sequence short before its End opcode.</summary>
    public void Cancel() => _complete = true;

    /// <summary>Advances exactly one frame, appending events to the shared list.</summary>
    /// <param name="events">List to which new events are appended.</param>
    private void AdvanceOneFrame(List<BattleSequenceEvent> events)
    {
        // Drain the active Wait. When the counter was already zero or just reached zero,
        // fall through to command processing in the same frame.
        if (_waitFrames > 0 && --_waitFrames > 0)
        {
            return;
        }

        // Process commands until the next Wait or End.
        while (!_complete && _cursor < _script.Commands.Count)
        {
            var command = _script.Commands[_cursor];
            _cursor++;
            bool blocked = ProcessCommand(command, events);
            if (blocked)
            {
                return; // Wait started; stop advancing this frame.
            }
        }

        if (!_complete)
        {
            // Script has no End opcode — treat as AlternateEnd.
            EmitEnd(events);
        }
    }

    /// <summary>Dispatches one command, appending its event and returning whether it blocked the timeline.</summary>
    /// <param name="command">Command to process.</param>
    /// <param name="events">Output event list.</param>
    /// <returns>True when the command started a Wait that blocks further commands this frame.</returns>
    private bool ProcessCommand(BattleActionCommand command, List<BattleSequenceEvent> events)
    {
        switch (command.Opcode)
        {
            case BattleActionOpcode.End:
            case BattleActionOpcode.AlternateEnd:
                EmitEnd(events);
                return false;

            case BattleActionOpcode.HitDispatch:
                events.Add(new HitDispatchedEvent(
                    _context.AttackerSide,
                    _context.AttackerId,
                    _context.TargetId,
                    _context.HitEvents,
                    command.Arguments.Count > 0 ? command.Arguments[0] : 0));
                return false;

            case BattleActionOpcode.Model:
                events.Add(BuildModelEvent(command));
                return false;

            case BattleActionOpcode.SpriteEffect:
                events.Add(new SpriteEffectEvent(_context.AttackerSide, command.Arguments));
                return false;

            case BattleActionOpcode.Stage:
                events.Add(new StageChangeEvent(command.Arguments));
                return false;

            case BattleActionOpcode.Camera:
                events.Add(new CameraRequestEvent(_context.AttackerSide, command.Arguments));
                return false;

            case BattleActionOpcode.EffectModel:
                events.Add(new EffectModelEvent(_context.AttackerSide, command.Arguments));
                return false;

            case BattleActionOpcode.Sound:
                int soundId = command.Arguments.Count > 0 ? command.Arguments[0] : 0;
                int timing = command.Arguments.Count > 1 ? command.Arguments[1] : 0;
                events.Add(new SoundRequestEvent(_context.AttackerSide, soundId, timing));
                return false;

            case BattleActionOpcode.Wait:
                int waitCount = command.Arguments.Count > 0 ? command.Arguments[0] : 0;

                // A Wait of 0 frames is a no-op; positive values block the timeline.
                if (waitCount > 0)
                {
                    _waitFrames = waitCount - 1; // current frame already consumed
                    return true;
                }

                return false;

            default:
                // Unknown opcode from future family — skip silently per scope contract.
                return false;
        }
    }

    /// <summary>Marks the sequence complete and appends the SequenceEnded event.</summary>
    /// <param name="events">Output event list.</param>
    private void EmitEnd(List<BattleSequenceEvent> events)
    {
        _complete = true;
        events.Add(new SequenceEndedEvent(_context.AttackerSide, _context.AttackerId, _context.TargetId));
    }

    /// <summary>Builds a ModelMotionEvent from a Model command's arguments.</summary>
    /// <param name="command">Model command.</param>
    /// <returns>The typed event.</returns>
    private ModelMotionEvent BuildModelEvent(BattleActionCommand command)
    {
        int sub = command.Arguments.Count > 0 ? command.Arguments[0] : 0;
        int actor = command.Arguments.Count > 1 ? command.Arguments[1] : 0;
        int motion = sub == 0 && command.Arguments.Count > 2 ? command.Arguments[2] : 0;
        return new ModelMotionEvent(_context.AttackerSide, sub, actor, motion);
    }
}
