namespace Dw3Production.Simulation;

/// <summary>Reads scheduler inputs from optional active fighters.</summary>
internal static class BattleFighterTiming
{
    /// <summary>Gets a fighter speed with an empty-side fallback accepted by the delay calculator.</summary>
    /// <param name="fighter">Fighter whose speed is read.</param>
    /// <returns>The raw speed, or zero when no fighter is active.</returns>
    internal static int SpeedOf(BattleFighter? fighter) => fighter?.Stats.Stats[BattleStat.Speed] ?? 0;

    /// <summary>Gets a fighter spirit with an empty-side fallback.</summary>
    /// <param name="fighter">Fighter whose spirit is read.</param>
    /// <returns>The raw spirit, or zero when no fighter is active.</returns>
    internal static int SpiritOf(BattleFighter? fighter) => fighter?.Stats.Stats[BattleStat.Spirit] ?? 0;
}
