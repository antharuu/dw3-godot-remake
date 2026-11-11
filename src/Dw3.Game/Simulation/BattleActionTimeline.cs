using Dw3.Data.Battle;

namespace Dw3Production.Simulation;

/// <summary>Engine-free action playback with pre-resolved hits, physical readiness and consumer acknowledgements.</summary>
public sealed class BattleActionTimeline
{
    private readonly BattleActionScript _script;
    private readonly ActionSequenceContext _context;
    private int _cursor;
    private int _soundsPlayed;
    private int _waitFrames;
    private bool _complete;
    private BattleSequenceEvent? _pendingRequest;

    /// <summary>Initializes a new instance of the <see cref="BattleActionTimeline"/> class.</summary>
    /// <param name="script">Bounded action commands.</param>
    /// <param name="context">Frozen identity and pre-resolved hits.</param>
    public BattleActionTimeline(BattleActionScript script, ActionSequenceContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        BattleActionValidation.Validate(script);
        _script = new BattleActionScript(script.Entry, [.. script.Commands.Select(c => new BattleActionCommand(c.Opcode, [.. c.Arguments]))]);
        _context = context;
    }

    /// <summary>Gets or sets the physical completion callback. Missing models are not ready.</summary>
    public Func<int, bool> MotionReady { get; set; } = _ => false;

    /// <summary>Gets or sets the independent jump-child readiness callback.</summary>
    public Func<int, bool> JumpReady { get; set; } = _ => true;

    /// <summary>Gets or sets consumer acknowledgement. False keeps the command pending without publishing it.</summary>
    public Func<BattleSequenceEvent, bool> Acknowledge { get; set; } = _ => true;

    /// <summary>Gets or sets completion of active reaction and sprite children.</summary>
    public Func<bool> ChildrenReady { get; set; } = () => true;

    /// <summary>Gets the frozen action identity.</summary>
    public ActionSequenceContext Context => _context;

    /// <summary>Gets a value indicating whether End and all required children completed.</summary>
    public bool IsComplete => _complete;

    /// <summary>Gets the next unconsumed command index.</summary>
    public int Cursor => _cursor;

    /// <summary>Gets the remaining explicit wait units.</summary>
    public int WaitFrames => _waitFrames;

    /// <summary>Advances deterministic script ticks. Physical callbacks remain the authority for readiness.</summary>
    /// <param name="frames">Positive tick count, not a measurement of source PAL cadence.</param>
    /// <returns>Only events acknowledged by their consumers, in script order.</returns>
    public IReadOnlyList<BattleSequenceEvent> Advance(int frames = 1)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(frames, 1);
        var events = new List<BattleSequenceEvent>();
        for (int f = 0; f < frames && !_complete; f++)
        {
            AdvanceOneFrame(events);
        }

        return events;
    }

    /// <summary>Cancels playback without fabricating an End event. The owner must dispose its children.</summary>
    public void Cancel() => _complete = true;

    /// <summary>Processes one tick until a wait, pending acknowledgement or End.</summary>
    /// <param name="events">Acknowledged event output.</param>
    private void AdvanceOneFrame(List<BattleSequenceEvent> events)
    {
        if (_waitFrames > 0 && --_waitFrames > 0)
        {
            return;
        }

        while (!_complete)
        {
            var command = _script.Commands[_cursor];
            if (!Ready(command))
            {
                return;
            }

            var ev = _pendingRequest ??= BuildEvent(command);
            if (ev is not null && !Acknowledge(ev))
            {
                return;
            }

            _pendingRequest = null;
            _cursor++;
            if (ev is not null)
            {
                events.Add(ev);
                _complete = ev is SequenceEndedEvent;
            }

            if (command.Opcode == BattleActionOpcode.Wait)
            {
                _waitFrames = command.Arguments[0];
                return;
            }
        }
    }

    /// <summary>Tests blocking commands without requesting another motion or ending active children.</summary>
    /// <param name="command">Next command.</param>
    /// <returns>Whether it can be consumed now.</returns>
    private bool Ready(BattleActionCommand command) => command.Opcode switch
    {
        BattleActionOpcode.Model when command.Arguments is [0, var actor, 0] => MotionReady(actor),
        BattleActionOpcode.Model when command.Arguments is [5, var actor, 0] => JumpReady(actor),
        BattleActionOpcode.End or BattleActionOpcode.AlternateEnd => ChildrenReady(),
        _ => true,
    };

    /// <summary>Resolves hit-dependent indices from frozen context once per distinct opcode.</summary>
    /// <param name="index">Script sound index or substitution opcode.</param>
    /// <param name="duration">Source key-off ticks, zero for natural completion.</param>
    /// <returns>Stable resolved request, retained during pending retries.</returns>
    private SoundRequestEvent BuildSound(int index, int duration)
    {
        if (index is 0x62 or 0x63)
        {
            int hit = index == 0x63 ? 3 : Math.Min(_soundsPlayed, 3);
            index = _context.SoundResults[hit] == 3 ? 0x38 : _context.ScriptSound;
            _soundsPlayed++;
        }

        return new SoundRequestEvent(_context.AttackerSide, index, duration);
    }

    /// <summary>Builds the typed request without mutating rules or drawing RNG.</summary>
    /// <param name="command">Validated command.</param>
    /// <returns>Request, or null for a physical wait or explicit delay.</returns>
    private BattleSequenceEvent? BuildEvent(BattleActionCommand command)
    {
        var a = command.Arguments;
        return command.Opcode switch
        {
            BattleActionOpcode.End or BattleActionOpcode.AlternateEnd => new SequenceEndedEvent(_context.AttackerSide, _context.AttackerId, _context.TargetId),
            BattleActionOpcode.HitDispatch => new HitDispatchedEvent(_context.AttackerSide, _context.AttackerId, _context.TargetId, _context.HitEvents, a[0]),
            BattleActionOpcode.Model when a is [0, _, 0] => null,
            BattleActionOpcode.Model when a is [5, _, 0] => null,
            BattleActionOpcode.Model when a is [5, var actor, var kind, ..] => new ModelJumpEvent(_context.AttackerSide, actor, kind, a.Count == 4 ? a[3] : 0),
            BattleActionOpcode.Model when a[0] is 3 or 7 or 8 => new ModelTravelEvent(_context.AttackerSide, a),
            BattleActionOpcode.Model => new ModelMotionEvent(_context.AttackerSide, a[0], a[1], a.Count == 3 ? a[2] : 0),
            BattleActionOpcode.SpriteEffect => new SpriteEffectEvent(_context.AttackerSide, a),
            BattleActionOpcode.Stage => new StageChangeEvent(a),
            BattleActionOpcode.Camera => new CameraRequestEvent(_context.AttackerSide, a),
            BattleActionOpcode.EffectModel => new EffectModelEvent(_context.AttackerSide, a),
            BattleActionOpcode.Sound => BuildSound(a[0], a[1]),
            BattleActionOpcode.Wait => null,
            _ => throw new FormatException($"Unsupported action opcode {command.Opcode}."),
        };
    }
}
