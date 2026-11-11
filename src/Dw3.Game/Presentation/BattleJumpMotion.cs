using Godot;

namespace Dw3Production.Presentation;

/// <summary>Advances the source-qualified forward jump from normalized source-time units without changing battle rules.</summary>
public sealed class BattleJumpMotion
{
    private const int Completion = 4096;
    private static readonly int[] Heights = [640, 1280, 1920, 2560, 1280, 0];
    private static readonly int[] Speeds = [136, 102, 81, 64, 64, 42];
    private readonly Vector3 _home;
    private readonly float _travel;
    private readonly int _kind;
    private readonly Vector3 _start;
    private int _progress;

    /// <summary>Initializes a new instance of the <see cref="BattleJumpMotion"/> class for a kind-four jump from its model home position.</summary>
    /// <param name="home">Model home position in Godot coordinates.</param>
    /// <param name="kind">Source jump kind.</param>
    /// <param name="distance">Authored source distance.</param>
    /// <param name="enemy">Whether the model belongs to the enemy camp.</param>
    /// <param name="start">Current model position for a return jump.</param>
    public BattleJumpMotion(Vector3 home, int kind, int distance, bool enemy, Vector3? start = null)
    {
        if (kind is < 1 or > 6)
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        _home = home;
        _start = start ?? home;
        _kind = kind;
        float direction = enemy ? -1f : 1f;
        _travel = kind == 4
            ? -(distance + 0x2800) * direction / BattleSceneMath.Ps1UnitsPerGodotUnit
            : (start ?? home).Z - home.Z;
        Position = start ?? home;
    }

    /// <summary>Gets the current physical model-root position.</summary>
    public Vector3 Position { get; private set; }

    /// <summary>Gets a value indicating whether the jump has landed at its source-defined end position.</summary>
    public bool IsComplete { get; private set; }

    /// <summary>Advances exactly one normalized source-time unit.</summary>
    public void Step()
    {
        if (IsComplete)
        {
            return;
        }

        _progress += Speeds[_kind - 1];
        if (_progress >= Completion)
        {
            float endZ = _home.Z;
            if (_kind == 4)
            {
                endZ += _travel;
            }

            Position = _kind is 4 or 5 or 6
                ? new Vector3(_home.X, _home.Y, endZ)
                : new Vector3(_start.X, 0f, _start.Z);
            IsComplete = true;
            return;
        }

        float fraction = ForwardFraction();
        Position = PositionAt(fraction);
    }

    /// <summary>Composes the independent vertical reaction and travel channels.</summary>
    /// <param name="fraction">Quarter-sine travel fraction.</param>
    /// <returns>The current model-root position.</returns>
    private Vector3 PositionAt(float fraction) => _kind switch
    {
        4 => new Vector3(_home.X, _home.Y + ArcHeight(), _home.Z + (_travel * fraction)),
        5 => new Vector3(_home.X, _home.Y + ArcHeight(), _home.Z + (_travel * (1 - fraction))),
        6 => new Vector3(_start.X, _home.Y * fraction, _start.Z),
        _ => new Vector3(_start.X, (_start.Y * Mathf.Cos(Mathf.Pi * _progress / (Completion * 2f))) + ArcHeight(), _start.Z),
    };

    /// <summary>Gets the source half-sine vertical lift in Godot units.</summary>
    /// <returns>Vertical lift above the home position.</returns>
    private float ArcHeight() => Mathf.Sin(Mathf.Pi * _progress / Completion) * Heights[_kind - 1] / BattleSceneMath.Ps1UnitsPerGodotUnit;

    /// <summary>Gets the source quarter-sine forward fraction.</summary>
    /// <returns>Fraction of forward source travel.</returns>
    private float ForwardFraction() => Mathf.Sin(Mathf.Pi * _progress / (Completion * 2f));
}
