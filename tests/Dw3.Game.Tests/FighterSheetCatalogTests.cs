using Dw3Production.Diagnostics.Battle;
using Xunit;

namespace Dw3.Game.Tests;

/// <summary>Tests pack-derived fighter-sheet preflight classification with generated documents only.</summary>
public sealed class FighterSheetCatalogTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"dw3-fighter-catalog-{Guid.NewGuid():N}");

    /// <summary>Initializes a new instance of the <see cref="FighterSheetCatalogTests"/> class.</summary>
    public FighterSheetCatalogTests() => Directory.CreateDirectory(_root);

    /// <summary>Removes the generated-data directory.</summary>
    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>Requires the catalog to derive identifiers and all static failure families from one pack document.</summary>
    [Fact]
    public void InspectClassifiesMissingGeometryTextureAndMotionFromTheDocument()
    {
        var path = Path.Combine(_root, "fighter-77-model.json");
        var document = """{"fighter_id":77,"bones":[],"meshes":[],"textures":[{"image":"missing.png"}],"motions":[],"cameras":[{"proj":280},{"proj":280},{"proj":280}]}""";
        File.WriteAllText(path, document);

        var entry = FighterSheetCatalog.Inspect(path);

        Assert.Equal(77, entry.FighterId);
        Assert.Contains(FighterSheetFailure.GeometryMissing, entry.Failures);
        Assert.Contains(FighterSheetFailure.TextureMissing, entry.Failures);
        Assert.Contains(FighterSheetFailure.MotionMissing, entry.Failures);
        Assert.DoesNotContain(FighterSheetFailure.CameraMissing, entry.Failures);
    }

    /// <summary>Requires malformed documents to remain individual catalog records rather than aborting the scan.</summary>
    [Fact]
    public void InspectClassifiesUnreadableDocument()
    {
        var path = Path.Combine(_root, "fighter-88-model.json");
        File.WriteAllText(path, "not-json");

        var entry = FighterSheetCatalog.Inspect(path);

        Assert.Equal(88, entry.FighterId);
        Assert.Contains(FighterSheetFailure.DocumentInvalid, entry.Failures);
    }
}
