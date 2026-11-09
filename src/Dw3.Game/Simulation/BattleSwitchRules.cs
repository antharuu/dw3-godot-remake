namespace Dw3Production.Simulation;

/// <summary>Decides whether an active partner's temporary state can forbid a voluntary switch.</summary>
public static class BattleSwitchRules
{
    /// <summary>Returns whether a living active partner cannot voluntarily switch because of a control flag.</summary>
    /// <param name="active">The fighter currently occupying the active party slot.</param>
    /// <returns>True only for a living fighter carrying a switch-blocking flag.</returns>
    public static bool ActiveBlocksVoluntarySwitch(BattleFighter? active) =>
        active is { IsKnockedOut: false } && (active.Flags & (FighterFlags.NoSwitch | FighterFlags.Confused)) != 0;
}
