using System.Text.Json;
using Dw3Production.Presentation;

namespace Dw3Production.Conversion;

/// <summary>Validates a prepared effect-model manifest before a scene node is allocated.</summary>
internal static class BattleEffectModelManifestValidator
{
    /// <summary>Requires the prepared document to be a valid effect model with the expected source invariants.</summary>
    /// <param name="path">Resolved prepared manifest path.</param>
    /// <param name="effectId">Script-visible effect identifier used for diagnostics.</param>
    /// <exception cref="CacheResourceException">The available manifest cannot be trusted as an effect model.</exception>
    public static void Validate(string path, int effectId)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            JsonElement root = document.RootElement;
            BattleModelDocument.Validate(root);
            if (root.GetProperty("format").GetString() != "dw3-battle-effect-model-v1")
            {
                throw new InvalidOperationException("prepared document is not an effect model");
            }
        }
        catch (Exception error) when (error is IOException or JsonException or InvalidOperationException or FormatException or KeyNotFoundException)
        {
            throw new CacheResourceException("CACHE_INVALID", $"battle effect model {effectId} manifest invalid: {error.Message}");
        }
    }
}
