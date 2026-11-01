using System.Text.Json;

namespace Dw3.QualityRatchet;

/// <summary>
/// Reads the results of a SARIF 2.1 log written by the compiler through the ErrorLog property.
/// </summary>
internal static class SarifLogReader
{
    /// <summary>
    /// Reads the unsuppressed warning and error results of the log.
    /// Informational results and results silenced by a suppressor are not violations.
    /// </summary>
    /// <param name="path">The path of the SARIF log.</param>
    /// <returns>The violations in log order.</returns>
    public static IReadOnlyList<Violation> Read(string path)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            return
            [
                .. document.RootElement
                    .GetProperty("runs")
                    .EnumerateArray()
                    .SelectMany(ResultsOf)
                    .Where(IsViolation)
                    .Select(ToViolation),
            ];
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new QualityInputException($"Cannot read SARIF log {path}: {exception.Message}", exception);
        }
    }

    /// <summary>
    /// Lists the results of one run, a run without results having none.
    /// </summary>
    /// <param name="run">A run object of the log.</param>
    /// <returns>The result objects.</returns>
    private static IEnumerable<JsonElement> ResultsOf(JsonElement run)
    {
        return run.TryGetProperty("results", out JsonElement results)
            ? results.EnumerateArray()
            : Enumerable.Empty<JsonElement>();
    }

    /// <summary>
    /// Tells whether a result is an unsuppressed warning or error.
    /// </summary>
    /// <param name="result">A result object of the log.</param>
    /// <returns>True when the result counts as a violation.</returns>
    private static bool IsViolation(JsonElement result)
    {
        if (result.TryGetProperty("suppressions", out JsonElement suppressions) && suppressions.GetArrayLength() > 0)
        {
            return false;
        }

        string level = result.TryGetProperty("level", out JsonElement value) ? value.GetString() ?? "warning" : "warning";
        return level is "warning" or "error";
    }

    /// <summary>
    /// Converts a result object to a violation, using its first physical location.
    /// </summary>
    /// <param name="result">A result object of the log.</param>
    /// <returns>The violation.</returns>
    private static Violation ToViolation(JsonElement result)
    {
        string rule = result.GetProperty("ruleId").GetString() ?? string.Empty;
        if (!result.TryGetProperty("locations", out JsonElement locations) || locations.GetArrayLength() == 0)
        {
            return new Violation(rule, string.Empty, 0, 0);
        }

        JsonElement physical = locations[0].GetProperty("physicalLocation");
        string uri = physical.GetProperty("artifactLocation").GetProperty("uri").GetString() ?? string.Empty;
        JsonElement region = physical.TryGetProperty("region", out JsonElement value) ? value : default;
        return new Violation(rule, new Uri(uri).LocalPath, RegionNumber(region, "startLine"), RegionNumber(region, "startColumn"));
    }

    /// <summary>
    /// Reads a number of a region object, zero when the region or the number is absent.
    /// </summary>
    /// <param name="region">The region object, or the default element.</param>
    /// <param name="name">The property name.</param>
    /// <returns>The number.</returns>
    private static int RegionNumber(JsonElement region, string name)
    {
        return region.ValueKind == JsonValueKind.Object && region.TryGetProperty(name, out JsonElement value)
            ? value.GetInt32()
            : 0;
    }
}
