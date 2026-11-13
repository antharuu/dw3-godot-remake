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
        return input is null ? [] : Build(input.Partners, nameFor);
    }

    /// <summary>Maps the running report's current rows, or the resolved input before the report starts, to decoded names.</summary>
    /// <param name="report">Running report, or null.</param>
    /// <param name="input">Resolved report input, or null.</param>
    /// <param name="nameFor">Decoded-name resolver.</param>
    /// <returns>A fighter-id to display-name map.</returns>
    public static Dictionary<int, BattleTextLine> Build(
        FightReportController? report,
        FightReportInput? input,
        Func<int, BattleTextLine> nameFor) =>
        report is null ? Build(input, nameFor) : Build(report.Sources, nameFor);

    /// <summary>Maps every report partner, its equipped forms and the forms it learned to their decoded names.</summary>
    /// <param name="partners">Current report rows, settled once raised.</param>
    /// <param name="nameFor">Decoded-name resolver.</param>
    /// <returns>A fighter-id to display-name map.</returns>
    public static Dictionary<int, BattleTextLine> Build(
        IEnumerable<FightReportPartner> partners,
        Func<int, BattleTextLine> nameFor)
    {
        ArgumentNullException.ThrowIfNull(partners);
        ArgumentNullException.ThrowIfNull(nameFor);
        return partners
            .SelectMany(partner => partner.SlotDigimonIds.Concat(partner.LearnedForms.Select(form => form.DigimonId)).Prepend(partner.FighterId))
            .Distinct()
            .ToDictionary(id => id, nameFor);
    }
}
