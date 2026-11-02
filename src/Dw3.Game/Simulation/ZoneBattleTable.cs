using Dw3Production.Conversion;

namespace Dw3Production.Simulation;

/// <summary>
/// Selects the four battle lists that a zone feeds to the encounter controller. The game uses
/// the first battle place of the zone's first stage battle table; story-dependent place
/// selection is not modelled yet.
/// </summary>
internal static class ZoneBattleTable
{
    /// <summary>Number of battle areas a zone's area plane can address.</summary>
    private const int AreaCount = 4;

    /// <summary>Number of battle slots in each area list.</summary>
    private const int BattlesPerArea = 8;

    /// <summary>
    /// Battle lists with no battle, used for zones without a battle table. Their count of 0
    /// selects the slowest battle rate, and safe zones never count battle steps anyway because
    /// they have no area plane.
    /// </summary>
    private static readonly BattleList[] EmptyBattles =
        [.. Enumerable.Range(0, AreaCount).Select(_ => new BattleList(0, [.. Enumerable.Range(0, BattlesPerArea).Select(_ => new AreaBattle(0, 0, 0))]))];

    /// <summary>Returns the battle lists of a zone.</summary>
    /// <param name="encounters">Encounter tables of the data pack, or <see langword="null"/> before they are loaded.</param>
    /// <param name="zone">Zone contract; its id is <c>S</c> followed by the stage number the tables are keyed by.</param>
    /// <returns>The zone's four area lists, or the empty lists when the zone has no complete battle place.</returns>
    public static IReadOnlyList<BattleList> For(EncounterData? encounters, Zone zone)
    {
        if (encounters is null || !encounters.Stages.TryGetValue(zone.Id[1..], out var stage))
        {
            return EmptyBattles;
        }

        var stageBattles = stage.StageBattles.Count > 0 ? stage.StageBattles[0] : null;
        var place = stageBattles is { Places.Count: > 0 } ? stageBattles.Places[0] : null;
        if (place is null || place.Areas.Count != AreaCount)
        {
            return EmptyBattles;
        }

        return [.. place.Areas.Select(area => new BattleList(
            area.Count,
            [.. area.Battles.Select(battle => new AreaBattle(battle.Encounter, battle.Stage, battle.Music))]))];
    }
}
