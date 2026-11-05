namespace Dw3Production.Simulation;

/// <summary>Provides the deterministic opening roll retained for callers that have not injected a battle stream.</summary>
internal sealed class BattleDefaultOpeningRng : IBattleRng
{
    /// <summary>Returns a roll that retains the previous player-first compatibility path.</summary>
    /// <returns>A raw value outside the normal equal-speed enemy-first threshold.</returns>
    public int NextValue() => 127;
}
