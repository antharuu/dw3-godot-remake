namespace Dw3Production.Simulation;

/// <summary>Provides a fixed compatibility roll for legacy and synthetic callers, never the production host or original random table.</summary>
internal sealed class BattleDefaultOpeningRng : IBattleRng
{
    /// <summary>Returns a roll that retains the previous player-first compatibility path.</summary>
    /// <returns>A raw value outside the normal equal-speed enemy-first threshold.</returns>
    public int NextValue() => 127;
}
