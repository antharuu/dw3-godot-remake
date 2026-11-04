using System.Text.Json;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>Builds the fighter-sheet registry from the current data pack and validates static resources.</summary>
public static class FighterSheetCatalog
{
    private const string Prefix = "fighter-";
    private const string Suffix = "-model.json";

    /// <summary>Discovers every fighter model in the pack directory in numeric fighter order.</summary>
    /// <param name="modelsDirectory">Directory containing decoded fighter model JSON files.</param>
    /// <returns>One preflight entry per discovered model file.</returns>
    public static IReadOnlyList<FighterSheetCatalogEntry> Discover(string modelsDirectory) =>
        [.. Directory.EnumerateFiles(modelsDirectory, $"{Prefix}*{Suffix}")
            .Select(Inspect)
            .OrderBy(entry => entry.FighterId)];

    /// <summary>Classifies one pack model without constructing renderer state.</summary>
    /// <param name="path">Absolute path to a decoded fighter model JSON file.</param>
    /// <returns>Its id, camp camera family and every independent static failure.</returns>
    public static FighterSheetCatalogEntry Inspect(string path)
    {
        var fallbackId = ParseFileNameId(path);
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            var fighterId = root.TryGetProperty("fighter_id", out var id) ? id.GetInt32() : fallbackId;
            var cameras = ArrayLength(root, "cameras");
            List<FighterSheetFailure> failures = [];
            AddGeometryFailure(root, failures);
            AddTextureFailure(root, path, failures);
            AddMotionFailure(root, failures);
            AddCameraFailure(cameras, failures);
            return new FighterSheetCatalogEntry(fighterId, path, cameras == 3, cameras, failures);
        }
        catch (Exception error) when (error is IOException or JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return new FighterSheetCatalogEntry(fallbackId, path, false, 0, [FighterSheetFailure.DocumentInvalid]);
        }
    }

    /// <summary>Reads an array's length or zero for a missing/non-array member.</summary>
    /// <param name="root">Object holding the member.</param>
    /// <param name="name">JSON member name.</param>
    /// <returns>The array length, or zero.</returns>
    private static int ArrayLength(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array ? value.GetArrayLength() : 0;

    /// <summary>Adds the missing-geometry classification for empty skeletons, meshes or primitive lists.</summary>
    /// <param name="root">Decoded model document.</param>
    /// <param name="failures">Mutable failure collection.</param>
    private static void AddGeometryFailure(JsonElement root, List<FighterSheetFailure> failures)
    {
        var meshes = root.TryGetProperty("meshes", out var value) && value.ValueKind == JsonValueKind.Array ? value : default;
        var hasPrimitive = meshes.ValueKind == JsonValueKind.Array && meshes.EnumerateArray().Any(HasPrimitive);
        if (ArrayLength(root, "bones") < 2 || !hasPrimitive)
        {
            failures.Add(FighterSheetFailure.GeometryMissing);
        }
    }

    /// <summary>Returns whether one mesh carries source triangles or polygons.</summary>
    /// <param name="mesh">Mesh object to inspect.</param>
    /// <returns>True when it has at least one drawable primitive.</returns>
    private static bool HasPrimitive(JsonElement mesh) => ArrayLength(mesh, "triangles") > 0 || ArrayLength(mesh, "polygons") > 0;

    /// <summary>Adds the missing-texture classification for absent or missing page images.</summary>
    /// <param name="root">Decoded model document.</param>
    /// <param name="modelPath">Path used to resolve sibling images.</param>
    /// <param name="failures">Mutable failure collection.</param>
    private static void AddTextureFailure(JsonElement root, string modelPath, List<FighterSheetFailure> failures)
    {
        if (!root.TryGetProperty("textures", out var textures) || textures.ValueKind != JsonValueKind.Array || textures.GetArrayLength() == 0)
        {
            failures.Add(FighterSheetFailure.TextureMissing);
            return;
        }

        var folder = Path.GetDirectoryName(modelPath) ?? string.Empty;
        if (textures.EnumerateArray().Any(texture => !HasImage(texture, folder)))
        {
            failures.Add(FighterSheetFailure.TextureMissing);
        }
    }

    /// <summary>Returns whether one page names an existing, non-empty sibling PNG.</summary>
    /// <param name="texture">Texture-page object to inspect.</param>
    /// <param name="folder">Model document directory.</param>
    /// <returns>True when the image can be loaded by the runtime.</returns>
    private static bool HasImage(JsonElement texture, string folder)
    {
        if (!texture.TryGetProperty("image", out var image) || image.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(image.GetString()))
        {
            return false;
        }

        var path = Path.Combine(folder, image.GetString()!);
        return File.Exists(path) && new FileInfo(path).Length > 0;
    }

    /// <summary>Adds the missing-motion classification for an empty recovered motion table.</summary>
    /// <param name="root">Decoded model document.</param>
    /// <param name="failures">Mutable failure collection.</param>
    private static void AddMotionFailure(JsonElement root, List<FighterSheetFailure> failures)
    {
        if (!root.TryGetProperty("motions", out var motions) || motions.ValueKind != JsonValueKind.Array || !motions.EnumerateArray().Any(step => step.GetArrayLength() > 0))
        {
            failures.Add(FighterSheetFailure.MotionMissing);
        }
    }

    /// <summary>Adds the missing-camera classification for a layout other than the source 3/12-view families.</summary>
    /// <param name="cameraCount">Decoded camera count.</param>
    /// <param name="failures">Mutable failure collection.</param>
    private static void AddCameraFailure(int cameraCount, List<FighterSheetFailure> failures)
    {
        if (cameraCount is not 3 and not 12)
        {
            failures.Add(FighterSheetFailure.CameraMissing);
        }
    }

    /// <summary>Extracts a stable fallback id from the model filename.</summary>
    /// <param name="path">Model document path.</param>
    /// <returns>The parsed id, or <see cref="int.MaxValue"/> when malformed.</returns>
    private static int ParseFileNameId(string path)
    {
        var name = Path.GetFileName(path);
        return name.StartsWith(Prefix, StringComparison.Ordinal) && name.EndsWith(Suffix, StringComparison.Ordinal)
            && int.TryParse(name[Prefix.Length..^Suffix.Length], out var fighter) ? fighter : int.MaxValue;
    }
}
