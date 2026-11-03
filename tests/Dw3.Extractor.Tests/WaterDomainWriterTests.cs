using System.Text.Json;
using Dw3.Data;
using Dw3.Formats.Water;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Dw3.Extractor.Tests;

public sealed class WaterDomainWriterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"dw3-water-{Guid.NewGuid():N}");

    [Fact]
    public void WritesTablesSplitImagesAndManifest()
    {
        var tile = new StageTile(1, 0, 4, 2, 0x47, 2, 0, 1, 6, 0, -3, 40, 41, 5, 0x800A6000);
        var frames = new[]
        {
            new StageObjectFrame(0x47, 0, new SpriteFrameImage(2, 1, -1, 2, 1, [0x001F, 0x801F])),
            new StageObjectFrame(0x47, 1, new SpriteFrameImage(1, 1, 0, 0, -1, [0x03E0])),
        };
        var zone = new ExtractedWaterZone(
            200,
            new StageObjectSet([new StageObject(3, tile, frames)], 2),
            new WaterZoneSources("aa", "bb", "cc"));
        var skipped = new SkippedWaterZone(226, "unsupported StageTile record index=5");

        new DataPackWriter(_root).Write(Metadata(), context => WaterDomainWriter.Write(context, [zone], [skipped]));

        string data = Path.Combine(_root, "data");
        using SqliteConnection connection = new DataPackReader(_root).OpenDatabase();
        Assert.Equal("200|aa|bb|cc|2", Row(connection, "SELECT stage_id, overlay_sha256, sheet_sha256, texture_sha256, archive_entries FROM water_zones"));
        Assert.Equal("226|unsupported StageTile record index=5", Row(connection, "SELECT stage_id, error FROM water_skipped_zones"));
        Assert.Equal(
            "200|3|1|0|4|2|71|2|0|1|6|0|-3|40|41|5|2148163584",
            Row(connection, "SELECT stage_id, object_index, visible, anim, margin, depth, frame, cycle, cycle_first, cycle_last, cycle_delay, clut_row, x, y, sort_y, cycle_time, source_address FROM water_objects"));
        Assert.Equal(
            "200|3|1|71|1|0|0|-1|water/S200/object-003-frame-47-clut-1-opaque.png|water/S200/object-003-frame-47-clut-1-stp.png",
            Row(connection, "SELECT stage_id, object_index, ordinal, frame_id, clut_row, offset_x, offset_y, abr, opaque_image, stp_image FROM water_object_frames WHERE ordinal = 1"));
        Assert.True(File.Exists(Path.Combine(data, "water", "S200", "object-003-frame-47-clut-0-stp.png")));

        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(data, "water", "manifest.json")));
        JsonElement root = manifest.RootElement;
        Assert.Equal("m1-stage-water-v3", root.GetProperty("schema").GetString());
        Assert.Equal("S226: unsupported StageTile record index=5", root.GetProperty("skipped_zones")[0].GetString());
        JsonElement record = root.GetProperty("zones").GetProperty("S200").GetProperty("objects")[0];
        Assert.Equal(3, record.GetProperty("index").GetInt32());
        Assert.Equal(0x800A6000u, record.GetProperty("source_address").GetUInt32());
        JsonElement first = record.GetProperty("frames")[0];
        Assert.Equal("object-003-frame-47-clut-0-opaque.png", first.GetProperty("opaque_path").GetString());
        Assert.Equal(-1, first.GetProperty("offset")[0].GetInt32());
        Assert.Equal(2, first.GetProperty("offset")[1].GetInt32());
        Assert.Equal(1, first.GetProperty("abr").GetInt32());
        Assert.Equal(2, root.GetProperty("zones").GetProperty("S200").GetProperty("sources").GetProperty("archive_entries").GetInt32());
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            // Release pooled connections before deletion; on Windows, the pool keeps
            // the file handle open past Dispose(), preventing Directory.Delete.
            SqliteConnection.ClearAllPools();
        }

        Directory.Delete(_root, recursive: true);
    }

    private static string Row(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        using SqliteDataReader reader = command.ExecuteReader();
        Assert.True(reader.Read());
        return string.Join('|', Enumerable.Range(0, reader.FieldCount).Select(index => Convert.ToString(reader.GetValue(index), System.Globalization.CultureInfo.InvariantCulture)));
    }

    private static DataPackMetadata Metadata() => new(
        "test", "0123456789012345678901234567890123456789", "synthetic", DateTimeOffset.Parse("2026-01-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
}
