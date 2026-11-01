namespace Dw3Production.Simulation;

/// <summary>
/// Supplies battle random values to the simulation. Every battle roll (critical hits,
/// run-away chances, enemy action conditions, growth swings) masks or reduces the raw
/// value itself, so an implementation only has to return non-negative integers.
/// </summary>
public interface IBattleRng
{
    /// <summary>Returns the next raw random value for a battle decision.</summary>
    /// <returns>A non-negative integer; callers reduce it with a mask or a modulo.</returns>
    int NextValue();
}
