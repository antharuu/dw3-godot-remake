using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dw3.QualityRatchet;

/// <summary>
/// Loads tools/quality-baseline.json, a camelCase JSON document of the form
/// <c>{ "coverage": { "minimumPercent": 80, "libraries": [ ... ] } }</c>.
/// The file is edited by hand: the minimum is a decision, not a measurement.
/// </summary>
internal static class BaselineFile
{
    /// <summary>
    /// The serializer settings of the baseline file. Unknown properties are rejected, so a stale
    /// <c>violations</c> section from the former lint ratchet fails loudly instead of being ignored.
    /// </summary>
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>
    /// Loads and validates a baseline file.
    /// </summary>
    /// <param name="path">The path of the baseline file.</param>
    /// <returns>The validated baseline.</returns>
    /// <exception cref="QualityInputException">When the file is missing, is not valid JSON, has an unknown property or fails validation.</exception>
    public static QualityBaseline Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new QualityInputException($"Baseline not found: {path}.");
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
    /// Rejects a baseline without a coverage section, with a minimum outside 0 to 100 or with no library.
    /// </summary>
    /// <param name="baseline">The deserialized baseline, null for a JSON null document.</param>
    /// <param name="path">The path of the baseline file, for error messages.</param>
    /// <returns>The validated baseline.</returns>
    /// <exception cref="QualityInputException">When one of the conditions above holds.</exception>
    private static QualityBaseline Validate(QualityBaseline? baseline, string path)
    {
        if (baseline?.Coverage?.Libraries is null)
        {
            throw new QualityInputException($"Baseline {path} needs a coverage section with minimumPercent and libraries.");
        }

        if (baseline.Coverage.MinimumPercent is < 0 or > 100)
        {
            throw new QualityInputException($"Baseline {path}: minimumPercent must lie between 0 and 100.");
        }

        if (baseline.Coverage.Libraries.Count == 0)
        {
            throw new QualityInputException($"Baseline {path}: the coverage section must list at least one library.");
        }

        return baseline;
    }
}
