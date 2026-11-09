using Dw3Production.Session;

namespace Dw3Production.Simulation;

/// <summary>Builds the normal Digivolution menu from the persistent forms equipped by one partner.</summary>
public static class DigivolutionEligibility
{
    /// <summary>Returns the base form followed by explicit equipped slots without learning-rule reevaluation.</summary>
    /// <param name="partner">Persistent partner owning the selectable forms.</param>
    /// <returns>Selectable forms in stable source-slot order.</returns>
    public static IReadOnlyList<int> AvailableForms(PlayerSessionPartner partner)
    {
        ArgumentNullException.ThrowIfNull(partner);
        return [.. new[] { partner.OriginFormId }
            .Concat(partner.EquippedForms)
            .Where(form => form != 0)
            .Distinct()];
    }
}
