using Dw3Production.Conversion;

namespace Dw3Production.Simulation;

/// <summary>Builds the decoded fighter-name lookup consumed by the detailed fight report.</summary>
public static class FightReportNameCatalog
{
    /// <summary>Maps every resolved report partner to its decoded name.</summary>
    /// <param name="input">Detailed report input, or null before victory resolution.</param>
    /// <param name="nameFor">Decoded-name resolver.</param>
    /// <returns>A fighter-id to display-name map.</returns>
    public static Dictionary<int, BattleTextLine> Build(
        FightReportInput? input,
        Func<int, BattleTextLine> nameFor)
    {
        if (input is null)
        {
            return [];
        }

        return input.Partners
            .SelectMany(partner => partner.SlotDigimonIds.Prepend(partner.FighterId))
            .Distinct()
            .ToDictionary(id => id, nameFor);
    }
}
