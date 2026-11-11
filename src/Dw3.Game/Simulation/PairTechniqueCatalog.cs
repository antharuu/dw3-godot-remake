using Dw3Production.Conversion;

namespace Dw3Production.Simulation;

/// <summary>Creates pair-technique definitions from the decoded active-form pair fields.</summary>
public static class PairTechniqueCatalog
{
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
