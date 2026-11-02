using Dw3Production.Conversion;

namespace Dw3Production.Simulation;

/// <summary>Maps party and encounter fighter ids to decoded display-name records.</summary>
public static class BattleNameIdCatalog
{
    /// <summary>Builds the name-id map required by one encounter.</summary>
    /// <param name="party">Resolved partner fighter ids.</param>
    /// <param name="fighters">Decoded fighter metadata.</param>
    /// <param name="encounters">Decoded encounter metadata.</param>
    /// <param name="encounterIndex">Encounter whose enemy ids should be included.</param>
    /// <returns>A fighter-id to name-id map.</returns>
    public static Dictionary<int, int> Build(
        IReadOnlyList<int> party,
        FighterData fighters,
        EncounterData encounters,
        int encounterIndex)
    {
        var nameIds = new Dictionary<int, int>();
        AddPartners(nameIds, party, fighters);
        AddEnemies(nameIds, fighters, encounters, encounterIndex);
        return nameIds;
    }

    /// <summary>Adds partner names from the Digimon table.</summary>
    /// <param name="nameIds">Map under construction.</param>
    /// <param name="party">Partner fighter ids.</param>
    /// <param name="fighters">Decoded fighter metadata.</param>
    private static void AddPartners(Dictionary<int, int> nameIds, IReadOnlyList<int> party, FighterData fighters)
    {
        foreach (int id in party)
        {
            if (fighters.Digimon.TryGetValue(id, out DigimonData? partner))
            {
                nameIds[id] = partner.NameId;
            }
        }
    }

    /// <summary>Adds encounter enemies from the battle-fighter table.</summary>
    /// <param name="nameIds">Map under construction.</param>
    /// <param name="fighters">Decoded fighter metadata.</param>
    /// <param name="encounters">Decoded encounter metadata.</param>
    /// <param name="encounterIndex">Encounter to inspect.</param>
    private static void AddEnemies(
        Dictionary<int, int> nameIds,
        FighterData fighters,
        EncounterData encounters,
        int encounterIndex)
    {
        Dictionary<int, BattleTableEntry> table = fighters.BattleTable.ToDictionary(entry => entry.Id);
        EncounterEntry? encounter = encounters.Encounters.FirstOrDefault(candidate => candidate.Index == encounterIndex);
        if (encounter is null)
        {
            return;
        }

        foreach (int fighterId in encounter.Fighters.Where(fighter => fighter != 0))
        {
            if (table.TryGetValue(fighterId, out BattleTableEntry? enemy))
            {
                nameIds[fighterId] = enemy.NameId;
            }
        }
    }
}
