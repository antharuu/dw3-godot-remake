using System.Text.Json;
using Dw3Production.Conversion;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>Recounts the prepared sprite registry through the production catalog and writes it privately.</summary>
internal static class BattleSpriteRegistryRecount
{
    /// <summary>Indented output for private review.</summary>
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>Resolves every indexed identity through the runtime parser and records its playable shape.</summary>
    /// <param name="catalog">Production catalog.</param>
    /// <param name="path">Private output file.</param>
    public static void Write(BattleSpriteCatalog catalog, string path)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var resolutions = catalog.Index.Keys.Order().Select(catalog.Resolve).ToArray();
        var document = new
        {
            schema = "b9056-runtime-sprite-registry-v1",
            ids = resolutions.Length,
            byAvailability = resolutions.GroupBy(row => row.Availability.ToString()).ToDictionary(group => group.Key, group => group.Count()),
            rows = resolutions.Select(Describe).ToArray(),
        };
        File.WriteAllText(path, JsonSerializer.Serialize(document, Indented));
    }

    /// <summary>Describes one resolution.</summary>
    /// <param name="resolution">Runtime resolution.</param>
    /// <returns>A JSON-compatible row.</returns>
    private static object Describe(BattleSpriteResolution resolution)
    {
        if (resolution.Sheet is not { } sheet)
        {
            return new { id = resolution.Id, availability = resolution.Availability.ToString(), reason = resolution.Reason };
        }

        var parts = sheet.Frames.Values.SelectMany(frame => frame.Parts).ToArray();
        return new
        {
            id = sheet.Id,
            availability = resolution.Availability.ToString(),
            animations = sheet.Animations.Count,
            samples = sheet.Animations.Sum(animation => animation.Count),
            frames = sheet.Frames.Count,
            parts = parts.Length,
            clearedVramParts = parts.Count(part => part.PaletteSource == "cleared-vram"),
            abr = sheet.Frames.Values.Select(frame => frame.Abr).Distinct().Order().ToArray(),
        };
    }
}
