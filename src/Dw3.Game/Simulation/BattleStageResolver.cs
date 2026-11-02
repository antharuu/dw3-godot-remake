using Dw3Production.Conversion;

namespace Dw3Production.Simulation;

/// <summary>Resolves the fight-stage id attached to an encounter in the extracted field tables.</summary>
public static class BattleStageResolver
{
    /// <summary>The known stage used by the standalone debug encounter when no field table names it.</summary>
    public const int DebugStage = 18;

    /// <summary>Finds the first area battle that references an encounter.</summary>
    /// <param name="encounters">Extracted field encounter tables, or null before startup completes.</param>
    /// <param name="encounterIndex">Encounter id to find.</param>
    /// <returns>The referenced fight-stage id, or <see cref="DebugStage"/> when absent.</returns>
    public static int Resolve(EncounterData? encounters, int encounterIndex)
    {
        if (encounters is null)
        {
            return DebugStage;
        }

        return Find(encounters, encounterIndex) ?? DebugStage;
    }

    /// <summary>Searches the nested stage, place and area tables without assigning a fallback.</summary>
    /// <param name="encounters">Extracted field encounter tables.</param>
    /// <param name="encounterIndex">Encounter id to find.</param>
    /// <returns>The referenced stage, or null when no area names the encounter.</returns>
    private static int? Find(EncounterData encounters, int encounterIndex)
    {
        foreach (EncounterStage stage in encounters.Stages.Values)
        {
            int? result = Find(stage, encounterIndex);
            if (result is not null)
            {
                return result;
            }
        }

        return null;
    }

    /// <summary>Searches one field stage's battle arrays.</summary>
    /// <param name="stage">Field stage encounter table.</param>
    /// <param name="encounterIndex">Encounter id to find.</param>
    /// <returns>The referenced stage, or null.</returns>
    private static int? Find(EncounterStage stage, int encounterIndex)
    {
        IEnumerable<EncounterAreaBattle> battles = stage.StageBattles
            .SelectMany(array => array.Places)
            .SelectMany(place => place.Areas)
            .SelectMany(area => area.Battles);
        EncounterAreaBattle? match = battles.FirstOrDefault(battle => battle.Encounter == encounterIndex);
        return match?.Stage;
    }
}
