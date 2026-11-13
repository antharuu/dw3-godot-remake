using System.Text.Json.Nodes;
using Dw3.Data;
using Dw3.Data.Battle;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Dw3.Extractor.Tests;

public sealed class SpriteEffectContractTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"dw3-sprite-contract-{Guid.NewGuid():N}");

    [Theory]
    [InlineData("version")]
    [InlineData("identity")]
    [InlineData("sheet")]
    [InlineData("placement")]
    [InlineData("texture")]
    [InlineData("geometry")]
    [InlineData("palette")]
    [InlineData("sample")]
    [InlineData("duration")]
    [InlineData("duplicate")]
    [InlineData("unsupported")]
    [InlineData("bounds")]
    [InlineData("hash")]
    [InlineData("source")]
    [InlineData("cleared")]
    [InlineData("vram")]
    public void LoaderRejectsCorruptPreparedContracts(string fault)
    {
        Extract();
        Mutate("battle_sprites_v1", "effect_id=123", Mutations()[fault]);
        Assert.Throws<InvalidDataException>(() => SpriteEffectDomainReader.Load(new DataPackReader(_root)));
    }

    [Fact]
    public void LoaderRejectsInvalidFadeSourceSignature()
    {
        Extract();
        Mutate("battle_sprite_scripts_v1", "selector=0", node => node["commands"]![0]!["arguments"]![0] = 2);
        Assert.Throws<InvalidDataException>(() => SpriteEffectScriptReader.Load(new DataPackReader(_root)));
    }

    [Fact]
    public void VerifyCommandExercisesPreparedReaderAndUsageWithoutOpeningDisc()
    {
        Extract();
        using var output = new StringWriter();
        Assert.Equal(0, Cli.Run(["verify-sprites", _root], output, TextWriter.Null));
        Assert.Contains("battle-sprites-v2", output.ToString());
        Assert.Equal(2, Cli.Run(["verify-sprites"], TextWriter.Null, TextWriter.Null));
        Assert.Equal(1, Cli.Run(["verify-sprites", Path.Combine(_root, "absent")], TextWriter.Null, TextWriter.Null));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    private static Dictionary<string, Action<JsonNode>> Mutations() => new(StringComparer.Ordinal)
    {
        ["version"] = n => n["schema"] = "wrong",
        ["identity"] = n => n["reference"]!["EffectId"] = 999,
        ["sheet"] = n => n["sheet"]!["Index"] = 999,
        ["placement"] = n => n["sheet"]!["Placement"] = new JsonArray(1),
        ["texture"] = n => n["textureWords"]![0] = new JsonArray(),
        ["geometry"] = n => n["sourceFrames"]![0]!["placements"]![0]![0] = 999,
        ["palette"] = n => n["frames"]![0]!["parts"]![0]!["palette"] = new JsonArray(),
        ["sample"] = n => n["animations"]![0]![0]![0] = 999,
        ["duration"] = n => n["animations"]![0] = new JsonArray(),
        ["duplicate"] = n => n["frames"]!.AsArray().Add(n["frames"]![0]!.DeepClone()),
        ["unsupported"] = n => n["unsupportedFrames"] = new JsonArray(JsonNode.Parse("{\"frame\":8,\"row\":0,\"reason\":\"missing\"}")),
        ["bounds"] = n => n["frames"]![0]!["width"] = 0,
        ["hash"] = n => n["frames"]![0]!["sha256"] = "wrong",
        ["source"] = n => n["frames"]![0]!["parts"]![0]!["paletteSource"] = "borrowed-capture",
        ["cleared"] = n =>
        {
            n["frames"]![0]!["parts"]![0]!["paletteSource"] = "cleared-vram";
            n["frames"]![0]!["parts"]![0]!["vram"] = new JsonArray(496, 1);
        },
        ["vram"] = n => n["frames"]![0]!["parts"]![0]!["vram"] = new JsonArray(496),
    };

    private void Extract()
    {
        var (disc, _) = FabricatedSpriteEffects.Create();
        using var error = new StringWriter();
        Assert.True(Cli.Run(["extract", "--disc", "synthetic.cue", "--out", _root, "--only", "battle-sprites"], TextWriter.Null, error, _ => disc.Open()) == 0, error.ToString());
    }

    private void Mutate(string table, string condition, Action<JsonNode> mutate)
    {
        using var database = new SqliteConnection($"Data Source={Path.Combine(_root, "data", "dw3.sqlite")};Pooling=False");
        database.Open();
        using var command = database.CreateCommand();
        command.CommandText = $"SELECT payload FROM {table} WHERE {condition}";
        JsonNode node = JsonNode.Parse((string)command.ExecuteScalar()!)!;
        mutate(node);
        command.CommandText = $"UPDATE {table} SET payload=$payload WHERE {condition}";
        command.Parameters.AddWithValue("$payload", node.ToJsonString());
        command.ExecuteNonQuery();
    }
}
