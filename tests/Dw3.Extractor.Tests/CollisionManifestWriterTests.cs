using System.Text;
using System.Text.Json;
using Xunit;
using Dw3.Formats.Field;

namespace Dw3.Extractor.Tests;

/// <summary>Synthetic tests for the collision manifest serializer.</summary>
public sealed class CollisionManifestWriterTests
{
    [Fact]
    public void WritesExpectedJsonStructure()
    {
        var actors = new Dictionary<string, Dictionary<string, FieldActor>>
        {
            ["S200"] = new() { ["actor1"] = new FieldActor(564, 558, 32) },
            ["S235"] = new()
            {
                ["actor0"] = new FieldActor(188, 154, 32),
                ["actor1"] = new FieldActor(175, 192, 48),
            },
        };

        var sb = new StringBuilder();
        using var sw = new StringWriter(sb);
        CollisionManifestWriter.Write(sw, actors);
        string json = sb.ToString();

        using var doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;
        Assert.Equal("EXP-800", root.GetProperty("schema").GetString());
        Assert.Equal(32, root.GetProperty("actors").GetProperty("S200").GetProperty("actor1").GetProperty("width").GetInt32());
        Assert.Equal(564, root.GetProperty("actors").GetProperty("S200").GetProperty("actor1").GetProperty("x").GetInt32());
        Assert.Equal(558, root.GetProperty("actors").GetProperty("S200").GetProperty("actor1").GetProperty("y").GetInt32());
        Assert.Equal(188, root.GetProperty("actors").GetProperty("S235").GetProperty("actor0").GetProperty("x").GetInt32());
        Assert.Equal(48, root.GetProperty("actors").GetProperty("S235").GetProperty("actor1").GetProperty("width").GetInt32());
    }
}
