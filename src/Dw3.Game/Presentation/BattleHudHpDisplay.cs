namespace Dw3Production.Presentation;

/// <summary>Tracks the two source HUD HP displays, including their delayed 30-frame sine easing.</summary>
public sealed class BattleHudHpDisplay
{
    private const ulong SampleIntervalMilliseconds = 133;
    private const ulong TweenDurationMilliseconds = 500;
    private readonly Slot[] _slots = [new(), new()];

    /// <summary>Gets the displayed HP for a side at a particular presentation time.</summary>
    /// <param name="side">Player side zero or enemy side one.</param>
    /// <param name="fighter">The currently active fighter.</param>
    /// <param name="milliseconds">Presentation clock in milliseconds.</param>
    /// <returns>The clamped, source-tweened HP value.</returns>
    public int Value(int side, BattleFighterView fighter, ulong milliseconds)
    {
        var slot = _slots[side];
        var target = ClampHp(fighter);
        if (slot.FighterId != fighter.Id)
        {
            slot.Reset(fighter.Id, target, milliseconds);
            return target;
        }

        slot.Step(milliseconds);
        if (milliseconds - slot.LastSampleMilliseconds >= SampleIntervalMilliseconds && target != slot.Target)
        {
            slot.Begin(target, milliseconds);
        }

        return slot.Value;
    }

    /// <summary>Clamps a fighter's current HP to its legal display range.</summary>
    /// <param name="fighter">Fighter providing the source HP values.</param>
    /// <returns>The source-clamped HP.</returns>
    private static int ClampHp(BattleFighterView fighter) => Math.Clamp(fighter.Hp, 0, Math.Max(0, fighter.MaxHp));

    /// <summary>Mutable source-tween state for one HUD side.</summary>
    private sealed class Slot
    {
        private int _fighterId;

        private int _target;

        private int _value;

        private ulong _lastSampleMilliseconds;

        private int _from;

        private ulong _startedMilliseconds;

        private bool _active;

        /// <summary>Gets the currently active fighter identifier.</summary>
        public int FighterId => _fighterId;

        /// <summary>Gets the sampled target HP.</summary>
        public int Target => _target;

        /// <summary>Gets the current displayed HP.</summary>
        public int Value => _value;

        /// <summary>Gets the time of the last source HP sample.</summary>
        public ulong LastSampleMilliseconds => _lastSampleMilliseconds;

        /// <summary>Resets the display immediately for a newly active fighter.</summary>
        /// <param name="fighterId">The active fighter identifier.</param>
        /// <param name="hp">The clamped starting HP.</param>
        /// <param name="milliseconds">Presentation clock in milliseconds.</param>
        public void Reset(int fighterId, int hp, ulong milliseconds)
        {
            _fighterId = fighterId;
            _from = hp;
            _target = hp;
            _value = hp;
            _lastSampleMilliseconds = milliseconds;
            _startedMilliseconds = milliseconds;
            _active = false;
        }

        /// <summary>Advances the current source sine tween to a presentation time.</summary>
        /// <param name="milliseconds">Presentation clock in milliseconds.</param>
        public void Step(ulong milliseconds)
        {
            if (!_active)
            {
                return;
            }

            var elapsed = milliseconds - _startedMilliseconds;
            if (elapsed >= TweenDurationMilliseconds)
            {
                _value = _target;
                _active = false;
                return;
            }

            var progress = Math.Sin((elapsed * Math.PI) / (2 * TweenDurationMilliseconds));
            _value = _from + (int)((_target - _from) * progress);
        }

        /// <summary>Begins a new source HP tween from the displayed value.</summary>
        /// <param name="target">The newly sampled clamped HP.</param>
        /// <param name="milliseconds">Presentation clock in milliseconds.</param>
        public void Begin(int target, ulong milliseconds)
        {
            _from = _value;
            _target = target;
            _startedMilliseconds = milliseconds;
            _lastSampleMilliseconds = milliseconds;
            _active = true;
        }
    }
}
