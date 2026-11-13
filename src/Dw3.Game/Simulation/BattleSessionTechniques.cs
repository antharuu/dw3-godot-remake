using Dw3Production.Conversion;
using Dw3Production.Session;

namespace Dw3Production.Simulation;

/// <summary>Selects session-owned actions while granting an unlearned temporary form its signature technique.</summary>
internal static class BattleSessionTechniques
{
    /// <summary>Filters one form's decoded actions without adding the Blast form to acquired or equipped slots.</summary>
    /// <remarks>A skill a form acquired from its own levels is usable by that form only.</remarks>
    /// <param name="partner">Permanent owner of learned forms and techniques.</param>
    /// <param name="fighter">Current battle fighter selecting the temporary signature exception.</param>
    /// <param name="form">Form whose actions are being prepared.</param>
    /// <param name="fighters">Pack-loaded form catalogue.</param>
    /// <param name="techs">Pack-loaded technique catalogue.</param>
    /// <param name="text">Technique text catalogue.</param>
    /// <returns>Allowed technique-menu entries for this form.</returns>
    public static IReadOnlyList<BattleTechnique> Resolve(PlayerSessionPartner partner, BattleFighter? fighter, int form, FighterData fighters, TechniqueData techs, BattleTextCatalog text)
    {
        IReadOnlyList<BattleTechnique> actions = BattleSetupFactory.PlayerTechniques(fighters, techs, form, text);
        int signature = 0;
        bool temporary = fighter is { Temporary: true } && fighter.Id == form;
        if (temporary && fighters.Digimon.TryGetValue(form, out DigimonData? data) && data.Skills.Count > 6)
        {
            signature = BattleSetupFactory.SkillId(data.Skills[6]);
        }

        bool learnedForm = !temporary || partner.KnownForms.Contains(form);
        IReadOnlyList<int> formTechniques = partner.FormTechniques(form);
        return [.. actions.Where(action => action.Id == signature || (learnedForm && (partner.KnownTechniques.Contains(action.Id) || formTechniques.Contains(action.Id))))];
    }
}
