using Dw3Production.Simulation;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>Counts and records controlled simulation draws so proofs can assert their exact order.</summary>
internal sealed class BattleAttackProofRng : IBattleRng
{
    private readonly int _value;
    private readonly Queue<int> _scripted;
    private readonly List<int> _draws = [];

    /// <summary>Initializes a new instance of the <see cref="BattleAttackProofRng"/> class.</summary>
    /// <param name="value">Controlled raw branch-setup value.</param>
    public BattleAttackProofRng(int value)
        : this(value, [])
    {
    }

    /// <summary>Initializes a new instance of the <see cref="BattleAttackProofRng"/> class with a scripted prefix.</summary>
    /// <param name="value">Controlled value returned once the scripted prefix is exhausted.</param>
    /// <param name="scripted">Values returned first, in their declared order.</param>
    public BattleAttackProofRng(int value, IEnumerable<int> scripted)
    {
        _value = value;
        _scripted = new Queue<int>(scripted);
    }

    /// <summary>Gets the number of simulation draws.</summary>
    public int Count => _draws.Count;

    /// <summary>Gets every value returned to the simulation, in consumption order.</summary>
    public IReadOnlyList<int> Draws => _draws;

    /// <inheritdoc/>
    public int NextValue()
    {
        int value = _scripted.Count > 0 ? _scripted.Dequeue() : _value;
        _draws.Add(value);
        return value;
    }

    /// <summary>Formats the draws consumed since a recorded position.</summary>
    /// <param name="from">Draw count recorded before the observed action.</param>
    /// <returns>Bracketed ordered values, for proof output.</returns>
    public string DrawsSince(int from) => "[" + string.Join(',', _draws.Skip(from)) + "]";
}
