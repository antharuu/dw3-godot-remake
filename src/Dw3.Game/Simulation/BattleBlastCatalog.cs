using Dw3Production.Conversion;
using Dw3Production.Session;

namespace Dw3Production.Simulation;

/// <summary>Resolves Blast table indices and form sources from the prepared fighter catalogue before battle startup.</summary>
internal static class BattleBlastCatalog
{
    /// <summary>Prepares every session slot independently, including slots sharing one permanent base identity.</summary>
    /// <param name="session">Persistent party whose canonical levels and equipment select Blast inputs.</param>
    /// <param name="fighters">Pack-loaded ordered Digimon records.</param>
    public static void Configure(PlayerSession session, FighterData fighters)
    {
        foreach (PlayerSessionPartner partner in session.Partners.OfType<PlayerSessionPartner>())
        {
            ConfigurePartner(partner, fighters);
        }
    }

    /// <summary>Resolves one partner's permanent identity before its current form can change.</summary>
    /// <param name="partner">Session slot being prepared.</param>
    /// <param name="fighters">Ordered source table used by one-based Blast entries.</param>
    private static void ConfigurePartner(PlayerSessionPartner partner, FighterData fighters)
    {
        int index = fighters.DigimonOrdered.Take(8).ToList().FindIndex(record => record.Id == partner.OriginFormId);
        if (index < 0)
        {
            return;
        }

        DigimonData origin = fighters.DigimonOrdered[index];
        int tier = BattleBlastGaugeRules.SelectTier([4, 19, 39, 70], partner.BattleSeed.Stats.Level);
        int formIndex = tier < origin.BlastForms.Count ? origin.BlastForms[tier] - 1 : -1;
        if (formIndex < 0 || formIndex >= fighters.DigimonOrdered.Count)
        {
            return;
        }

        int formId = fighters.DigimonOrdered[formIndex].Id;
        partner.BattleSeed.Blast = new(formId, index, [.. partner.EquipmentIds.Skip(4).Take(2)]);
        var sources = new Dictionary<int, BattleStats>(partner.BattleSeed.FormStats);
        foreach (int id in partner.KnownForms.Append(formId))
        {
            if (id != partner.OriginFormId && !sources.ContainsKey(id) && fighters.Digimon.TryGetValue(id, out DigimonData? form))
            {
                sources[id] = FormSource(partner.BattleSeed.Stats, form);
            }
        }

        partner.BattleSeed.FormStats = sources;
    }

    /// <summary>Adds the form's static contributions to persistent partner totals without replacing level or equipment.</summary>
    /// <param name="totals">Canonical partner totals, never a previously transformed snapshot.</param>
    /// <param name="form">Pack-loaded form contributions.</param>
    /// <returns>An independent source snapshot for the selected form.</returns>
    private static BattleStats FormSource(BattleStats totals, DigimonData form)
    {
        BattleStats result = BattleStatResolver.Resolve(totals, null, form.Id, 0, 0, []);
        for (int index = 0; index < 5; index++)
        {
            result.Stats[index] += form.BattleStats[index];
            result.Resist[7 + index] += form.StatusResists[index];
        }

        for (int index = 0; index < 7; index++)
        {
            result.Resist[index] = Math.Max(1, result.Resist[index] + form.Resistances[index]);
        }

        result.Family = form.Family;
        return result;
    }
}
