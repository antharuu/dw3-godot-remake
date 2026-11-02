using System.Numerics;

namespace Dw3Production.Simulation;

/// <summary>Counts movement frames and samples field encounter areas every eighth step.</summary>
public sealed class FieldEncounterStepper
{
    private int _movementFrames;

    /// <summary>Advances the movement counter and selects an encounter at the source cadence.</summary>
    /// <param name="areas">Field encounter-area plane, or null for a safe zone.</param>
    /// <param name="position">Player center in field world units.</param>
    /// <param name="encounters">Encounter selector with the active zone's battle tables.</param>
    /// <returns>A selected encounter on each eighth movement frame, otherwise null.</returns>
    public EncounterRecord? Tick(AreaMap? areas, Vector2 position, EncounterController encounters)
    {
        if (areas is null)
        {
            return null;
        }

        _movementFrames++;
        if ((_movementFrames & 7) != 0)
        {
            return null;
        }

        int areaCell = areas.CellAt(position);
        return encounters.CountBattleSteps(areaCell);
    }
}
