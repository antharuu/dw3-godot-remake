using System.Text.Json;
using Dw3.Data;
using Dw3.Formats.Field;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Dw3.Extractor.Tests;

public sealed class FieldcomDomainWriterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"dw3-fieldcom-{Guid.NewGuid():N}");

    [Fact]
    public void WritesCueAndIconManifestPngAndTables()
    {
        var cueImage = new FieldcomExtractedFrame(0x10, 1, 1, -8, -28, 0, -1, [255, 0, 0, 255]);
        var iconImage = new FieldcomExtractedFrame(0x23, 1, 1, 4, -36, 0, 2, [255, 0, 0, 255]);
        var cue = new FieldcomCueDomain("AAA/DAT/FIELD/FIELDCOM.BIN", "abcd", 42, [cueImage],
            [new FieldcomExitRow(0, 6)], [new FieldcomNamedSequence("inner-6", [new FieldcomFrameStep(0x10, 22)])],
            [new FieldcomNamedSequence("opening", [new FieldcomFrameStep(0x10, 1)]), new FieldcomNamedSequence("closing", [new FieldcomFrameStep(0x10, 1)])]);
        var icon = new FieldcomIconDomain("AAA/DAT/FIELD/FIELDCOM.BIN", "abcd", 42, [iconImage],
            [new FieldcomAnimation([new FieldcomFrameStep(0x23, 8)], 0)]);

        new DataPackWriter(_root).Write(Metadata(), context => FieldcomDomainWriter.Write(context, cue, icon));

        string data = Path.Combine(_root, "data");
        Assert.True(File.Exists(Path.Combine(data, "cue", "frame-10.png")));
        Assert.True(File.Exists(Path.Combine(data, "icon", "frame-23.png")));
        using SqliteConnection connection = new DataPackReader(_root).OpenDatabase();
        Assert.Equal(1L, Scalar(connection, "SELECT COUNT(*) FROM cue_exit_rows"));
        Assert.Equal(1L, Scalar(connection, "SELECT COUNT(*) FROM icon_animation_steps"));
        Assert.Equal(6L, Scalar(connection, "SELECT field_row FROM cue_sequences WHERE kind = 'inner'"));
        Assert.Equal(0L, Scalar(connection, "SELECT COUNT(*) FROM cue_sequences WHERE kind = 'pop' AND field_row IS NOT NULL"));
        using JsonDocument cueManifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(data, "cue", "manifest.json")));
        JsonElement cueFrame = cueManifest.RootElement.GetProperty("frames").GetProperty("16");
        Assert.Equal("frame-10.png", cueFrame.GetProperty("path").GetString());
        Assert.Equal(-1, cueFrame.GetProperty("abr").GetInt32());
        Assert.Equal(1, cueFrame.GetProperty("size")[0].GetInt32());
        Assert.Equal("FIELD_LAYER_MAP", cueManifest.RootElement.GetProperty("anchor").GetProperty("layer").GetString());
        Assert.True(cueManifest.RootElement.TryGetProperty("source", out _));
        Assert.True(cueManifest.RootElement.TryGetProperty("inner_rows", out _));
        using JsonDocument iconManifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(data, "icon", "manifest.json")));
        JsonElement iconSteps = iconManifest.RootElement.GetProperty("animations")[0].GetProperty("steps");
        Assert.Equal(0xFF, iconSteps[1][0].GetInt32());
        Assert.Equal(0, iconSteps[1][1].GetInt32());
        Assert.Equal("FIELD_LAYER_MAP", iconManifest.RootElement.GetProperty("anchor").GetProperty("layer").GetString());
        Assert.True(iconManifest.RootElement.TryGetProperty("loop_semantics", out _));
        Assert.True(iconManifest.RootElement.TryGetProperty("source", out _));
    }

    public void Dispose()
    {
        // Release pooled connections before deletion; on Windows, the pool keeps
        // the file handle open past Dispose(), preventing Directory.Delete.
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static long Scalar(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand(); command.CommandText = sql;
        return (long)command.ExecuteScalar()!;
    }

    private static DataPackMetadata Metadata() => new("test", "0123456789012345678901234567890123456789", "synthetic", DateTimeOffset.Parse("2026-01-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
}
