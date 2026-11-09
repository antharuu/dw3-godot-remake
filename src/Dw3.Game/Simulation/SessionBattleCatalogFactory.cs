using Dw3Production.Conversion;
using Dw3Production.Session;

namespace Dw3Production.Simulation;

/// <summary>Builds form and technique catalogues from the persistent session rather than debug defaults.</summary>
public static class SessionBattleCatalogFactory
{
    /// <summary>Configures the active session partner's selectable forms, actions and names on a battle flow.</summary>
    /// <param name="flow">Flow receiving the session-backed catalogues.</param>
    /// <param name="session">Persistent player session owning forms and techniques.</param>
    /// <param name="fighters">Decoded form and name data.</param>
    /// <param name="techs">Decoded technique data.</param>
    /// <param name="digivolutions">Optional extracted evolution requirements.</param>
    /// <param name="text">Decoded technique text.</param>
    public static void ConfigureActivePartner(
        BattleFlowController flow,
        PlayerSession session,
        FighterData fighters,
        TechniqueData techs,
        DigivolutionData? digivolutions,
        BattleTextData text)
    {
        ArgumentNullException.ThrowIfNull(flow);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(fighters);
        ArgumentNullException.ThrowIfNull(techs);
        ArgumentNullException.ThrowIfNull(text);

        PlayerSessionPartner? partner = session.Partners[0];
        if (partner is null)
        {
            return;
        }

        IReadOnlyList<int> forms = EligibleForms(partner, digivolutions);
        flow.PlayerDigivolutions = [.. forms.Where(form => form != partner.BattleSeed.Id)];
        flow.EnforceDigivolutionCatalog = true;
        ConfigureActions(flow, partner, forms, fighters, techs, new BattleTextCatalog(text));
        AddFormNames(flow, forms, fighters);
    }

    /// <summary>Returns owned forms filtered by extracted requirements when the production data is available.</summary>
    /// <param name="partner">Persistent active partner.</param>
    /// <param name="digivolutions">Optional extracted evolution requirements.</param>
    /// <returns>Owned eligible forms.</returns>
    private static IReadOnlyList<int> EligibleForms(PlayerSessionPartner partner, DigivolutionData? digivolutions)
    {
        PartnerDigivolution? source = digivolutions?.Partners.FirstOrDefault(entry => entry.BaseId == partner.OriginFormId);
        return source is null
            ? partner.KnownForms
            : DigivolutionEligibility.AvailableForms(source, partner.KnownForms, partner.BattleSeed.Stats);
    }

    /// <summary>Creates form-bound action catalogues and applies an explicit learned-technique restriction.</summary>
    /// <param name="flow">Flow receiving form action catalogues.</param>
    /// <param name="partner">Persistent active partner.</param>
    /// <param name="forms">Owned eligible forms.</param>
    /// <param name="fighters">Decoded form data.</param>
    /// <param name="techs">Decoded techniques.</param>
    /// <param name="text">Technique text catalogue.</param>
    private static void ConfigureActions(
        BattleFlowController flow,
        PlayerSessionPartner partner,
        IReadOnlyList<int> forms,
        FighterData fighters,
        TechniqueData techs,
        BattleTextCatalog text)
    {
        var basics = new Dictionary<int, TechData>();
        var techniques = new Dictionary<int, IReadOnlyList<BattleTechnique>>();
        var knownTechniques = new HashSet<int>(partner.KnownTechniques);
        foreach (int form in forms)
        {
            basics[form] = BattleSetupFactory.PlayerBasicTech(fighters, techs, form);
            IReadOnlyList<BattleTechnique> formTechniques = BattleSetupFactory.PlayerTechniques(fighters, techs, form, text);
            techniques[form] = knownTechniques.Count == 0
                ? formTechniques
                : [.. formTechniques.Where(technique => knownTechniques.Contains(technique.Id))];
        }

        flow.PlayerBasicTechByForm = basics;
        flow.PlayerTechniquesByForm = techniques;
    }

    /// <summary>Extends the flow name map with every selectable form's source name id.</summary>
    /// <param name="flow">Flow receiving form names.</param>
    /// <param name="forms">Owned eligible forms.</param>
    /// <param name="fighters">Decoded form data.</param>
    private static void AddFormNames(BattleFlowController flow, IReadOnlyList<int> forms, FighterData fighters)
    {
        var names = new Dictionary<int, int>(flow.NameIds);
        foreach (int form in forms)
        {
            if (fighters.Digimon.TryGetValue(form, out DigimonData? digimon))
            {
                names[form] = digimon.NameId;
            }
        }

        flow.NameIds = names;
    }
}
