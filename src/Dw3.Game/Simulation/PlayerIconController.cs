using System.Numerics;
using Dw3Production.Conversion;

namespace Dw3Production.Simulation;

/// <summary>The icon view the renderer consumes: the current frame index and the smooth
/// anchor position (world pixels) that the frame's own offset is applied to.</summary>
public sealed record IconView(int Frame, Vector2 Position);

/// <summary>
/// Drives the connection indicator floating over the player's head. It advances the
/// substate-0 animation at the source 50 Hz cadence, mapped from the fixed 60 Hz step
/// exactly like FieldRenderer.AdvanceSimulation maps the water/NPC source ticks, and it
/// follows the player with an exponential smoothing so the icon glides instead of
/// teleporting. The visual stays the decoded source frames; only the tracking is a
/// deliberate comfort adaptation (see docs/ICON_SMOOTH_FOLLOW.md), not source fidelity.
/// </summary>
public sealed class PlayerIconController
{
    /// <summary>Per-fixed-step exponential catch-up factor (0..1): each step the icon closes
    /// <see cref="FollowSmoothing"/> of the remaining gap to the player (no teleport).</summary>
    public const float FollowSmoothing = 0.35f;

    private PlayerIconAnimation _animation = null!;
    private int _step;
    private int _time;          // accumulated source ticks within the current step
    private int _frame;         // current frame index (0x23..)
    private Vector2 _position;  // smooth anchor position
    private bool _positioned;
    private int _simulationSteps;
    private int _lastSourceTick;

    public void SetAnimation(PlayerIconAnimation animation)
    {
        _animation = animation ?? throw new ArgumentNullException(nameof(animation));
        _step = 0;
        _time = 0;
        _frame = animation.Steps[0].Frame;
        _simulationSteps = 0;
        _lastSourceTick = 0;
    }

    /// <summary>The current frame + smooth position. Valid after <see cref="SetAnimation"/>.</summary>
    public IconView View => new(_frame, _position);

    /// <summary>Places the icon exactly on the player (spawn / warp), no glide-in.</summary>
    public void Reset(Vector2 playerPosition)
    {
        _step = 0;
        _time = 0;
        _frame = _animation.Steps[0].Frame;
        _position = playerPosition;
        _positioned = true;
        _simulationSteps = 0;
        _lastSourceTick = 0;
    }

    /// <summary>One fixed 60 Hz simulation step: advances the animation at the source 50 Hz
    /// cadence, then glides the anchor toward the player.</summary>
    public void Advance(Vector2 playerPosition)
    {
        _simulationSteps++;
        var sourceTick = _simulationSteps * 50 / 60;
        while (_lastSourceTick < sourceTick)
        {
            AdvanceSourceTick();
            _lastSourceTick++;
        }
        if (!_positioned)
        {
            _position = playerPosition;
            _positioned = true;
        }
        else
        {
            _position = Vector2.Lerp(_position, playerPosition, FollowSmoothing);
        }
    }

    // Animation step logic: each step has a duration; once elapsed, advance to the next step.
    // When the step index exceeds the last step, wrap to the loop target index.
    private void AdvanceSourceTick()
    {
        var steps = _animation.Steps;
        _time++;
        if (steps[_step].Duration < _time)
        {
            _time -= steps[_step].Duration;
            var next = _step + 1;
            _step = next >= steps.Count ? _animation.LoopTarget : next;
            _frame = steps[_step].Frame;
        }
    }
}
