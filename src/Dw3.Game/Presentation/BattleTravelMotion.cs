using Godot;

namespace Dw3Production.Presentation;

/// <summary>Interpolates a model root using a truncated twelve-bit progress increment.</summary>
public sealed class BattleTravelMotion
{
    private readonly Vector3 _start;
    private readonly Vector3 _target;
    private readonly int _step;
    private int _progress;

    /// <summary>Initializes a new instance of the <see cref="BattleTravelMotion"/> class.</summary>
    /// <param name="start">Current model-root position.</param>
    /// <param name="target">Authored destination in renderer coordinates.</param>
    /// <param name="time">Normalized source-time duration, zero for an immediate move.</param>
    public BattleTravelMotion(Vector3 start, Vector3 target, int time)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(time);
        _start = start;
        _target = target;
        _step = time == 0 ? 4096 : 4096 / time;
        Position = time == 0 ? target : start;
        IsComplete = time == 0;
    }

    /// <summary>Gets the current model-root position.</summary>
    public Vector3 Position { get; private set; }

    /// <summary>Gets a value indicating whether the authored destination was reached.</summary>
    public bool IsComplete { get; private set; }

    /// <summary>Advances one normalized source-time unit.</summary>
    public void Step()
    {
        if (IsComplete)
        {
            return;
        }

        _progress += _step;
        Position = _start.Lerp(_target, Math.Min(_progress, 4096) / 4096f);
        IsComplete = _progress + _step >= 4096;
        if (IsComplete)
        {
            Position = _target;
        }
    }
}
