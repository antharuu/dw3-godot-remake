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
    /// <param name="text">Decoded technique text.</param>
    public static void ConfigureActivePartner(
        BattleFlowController flow,
        PlayerSession session,
        FighterData fighters,
        TechniqueData techs,
        BattleTextData text)
    {
        ArgumentNullException.ThrowIfNull(flow);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(fighters);
        ArgumentNullException.ThrowIfNull(techs);
        ArgumentNullException.ThrowIfNull(text);

        flow.ActivePartnerCatalogRefresher = () => RefreshActivePartner(flow, session, fighters, techs, text);
    }

    /// <summary>Rebuilds the active fighter's forms, techniques and names from its matching session partner.</summary>
    /// <param name="flow">Flow receiving the active partner catalogue.</param>
    /// <param name="session">Persistent player session owning forms and techniques.</param>
    /// <param name="fighters">Decoded form and name data.</param>
    /// <param name="techs">Decoded technique data.</param>
    /// <param name="text">Decoded technique text.</param>
    private static void RefreshActivePartner(
        BattleFlowController flow,
        PlayerSession session,
        FighterData fighters,
        TechniqueData techs,
        BattleTextData text)
    {
        PlayerSessionPartner? partner = ActivePartner(flow, session);
        if (partner is null)
        {
            return;
        }

        IReadOnlyList<int> forms = EligibleForms(partner);
        flow.PlayerDigivolutions = forms;
        flow.EnforceDigivolutionCatalog = true;
        ConfigureActions(flow, partner, forms, fighters, techs, new BattleTextCatalog(text));
        AddFormNames(flow, forms, fighters);
    }

    /// <summary>Returns the active partner selected by the battle state.</summary>
    /// <param name="flow">Flow that owns the active party index.</param>
    /// <param name="session">Persistent session owning party slots.</param>
    /// <returns>The matching session partner, or null for an empty slot.</returns>
    private static PlayerSessionPartner? ActivePartner(BattleFlowController flow, PlayerSession session)
    {
        int active = flow.State.ActivePlayer;
        return active >= 0 && active < session.Partners.Length ? session.Partners[active] : null;
    }

    /// <summary>Returns forms already equipped by the active partner without reapplying learning requirements.</summary>
    /// <param name="partner">Persistent active partner.</param>
    /// <returns>Owned selectable forms.</returns>
    private static IReadOnlyList<int> EligibleForms(PlayerSessionPartner partner)
    {
        return DigivolutionEligibility.AvailableForms(partner);
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
