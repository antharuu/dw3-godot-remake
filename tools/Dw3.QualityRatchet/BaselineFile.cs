using System.Text.Json;

namespace Dw3.QualityRatchet;

/// <summary>
/// Loads and saves the baseline as indented JSON with a stable order, so each update is a readable diff.
/// </summary>
internal static class BaselineFile
{
    /// <summary>
    /// The serializer settings of the baseline file.
    /// </summary>
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    /// <summary>
    /// Loads and validates a baseline file.
    /// </summary>
    /// <param name="path">The path of the baseline file.</param>
    /// <returns>The baseline.</returns>
    public static QualityBaseline Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new QualityInputException($"Baseline not found: {path}. Create it once with the init command.");
        }

        QualityBaseline? baseline;
        try
        {
            baseline = JsonSerializer.Deserialize<QualityBaseline>(File.ReadAllText(path), Options);
        }
        catch (JsonException exception)
        {
            throw new QualityInputException($"Cannot read baseline {path}: {exception.Message}", exception);
        }

        return Validate(baseline, path);
    }

    /// <summary>
    /// Saves a baseline, violations ordered by key and libraries by name.
    /// </summary>
    /// <param name="path">The path of the baseline file.</param>
    /// <param name="baseline">The baseline to save.</param>
    public static void Save(string path, QualityBaseline baseline)
    {
        var ordered = new QualityBaseline(
            baseline.Coverage with { Libraries = [.. baseline.Coverage.Libraries.Order(StringComparer.Ordinal)] },
            [.. baseline.Violations.OrderBy(entry => entry.Key, ViolationKeyComparer.Instance)]);
        File.WriteAllText(path, JsonSerializer.Serialize(ordered, Options) + "\n");
    }

    /// <summary>
    /// Rejects a baseline with missing sections, a minimum outside 0 to 100, negative counts or duplicate keys.
    /// </summary>
    /// <param name="baseline">The deserialized baseline, null for a JSON null document.</param>
    /// <param name="path">The path of the baseline file, for error messages.</param>
    /// <returns>The validated baseline.</returns>
    private static QualityBaseline Validate(QualityBaseline? baseline, string path)
    {
        if (baseline?.Coverage?.Libraries is null || baseline.Violations is null)
        {
            throw new QualityInputException($"Baseline {path} needs a coverage section with minimumPercent and libraries, and a violations list.");
        }

        if (baseline.Coverage.MinimumPercent is < 0 or > 100)
        {
            throw new QualityInputException($"Baseline {path}: minimumPercent must lie between 0 and 100.");
        }

        BaselineViolation? negative = baseline.Violations.FirstOrDefault(entry => entry.Count < 0);
        if (negative is not null)
        {
            throw new QualityInputException($"Baseline {path}: {negative.Key} has a negative count.");
        }

        if (baseline.Violations.Select(entry => entry.Key).Distinct().Count() != baseline.Violations.Count)
        {
            throw new QualityInputException($"Baseline {path} lists the same rule, project and folder twice.");
        }

        return baseline;
    }
}
