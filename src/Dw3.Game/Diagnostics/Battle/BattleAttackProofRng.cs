using Dw3Production.Simulation;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>Counts controlled branch-setup rolls without drawing during physical playback.</summary>
internal sealed class BattleAttackProofRng : IBattleRng
{
    private readonly int _value;

    /// <summary>Initializes a new instance of the <see cref="BattleAttackProofRng"/> class.</summary>
    /// <param name="value">Controlled raw branch-setup value.</param>
    public BattleAttackProofRng(int value) => _value = value;

    /// <summary>Gets the number of simulation draws.</summary>
    public int Count { get; private set; }

    /// <inheritdoc/>
    public int NextValue()
    {
        Count++;
        return _value;
    }
}
