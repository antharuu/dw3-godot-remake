using Dw3Production.Conversion;

namespace Dw3Production.Simulation;

/// <summary>Creates pair-technique definitions from the decoded active-form pair fields.</summary>
public static class PairTechniqueCatalog
{
    /// <summary>Builds every source-qualified pair relation indexed by its active permanent form.</summary>
    /// <param name="fighters">Decoded fighter table from the user-owned data pack.</param>
    /// <returns>Relations whose two decoded pair fields are both present.</returns>
    public static IReadOnlyDictionary<int, PairTechniqueDefinition> Build(FighterData fighters)
    {
        ArgumentNullException.ThrowIfNull(fighters);
        var result = new Dictionary<int, PairTechniqueDefinition>();
        foreach (int fighterId in fighters.Digimon.Keys)
        {
            PairTechniqueDefinition? definition = ForActiveForm(fighters, fighterId);
            if (definition is not null)
            {
                result.Add(fighterId, definition);
            }
        }

        return result;
    }

    /// <summary>Returns the active form's configured pair technique, if it has one.</summary>
    /// <param name="fighters">Decoded fighter table from the user-owned data pack.</param>
    /// <param name="activeFormId">Current permanent or temporary active form id.</param>
    /// <returns>The configured technique id and required incoming name id, or <see langword="null"/>.</returns>
    public static PairTechniqueDefinition? ForActiveForm(FighterData fighters, int activeFormId)
    {
        ArgumentNullException.ThrowIfNull(fighters);
        if (!fighters.Digimon.TryGetValue(activeFormId, out DigimonData? active) || active.PairTech <= 0 || active.PairPartner <= 0)
        {
            return null;
        }

        return new PairTechniqueDefinition(active.PairTech, active.PairPartner);
    }
}
