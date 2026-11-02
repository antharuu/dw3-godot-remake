using System.Text.Json;
using Dw3.Data;
using Dw3.Formats.Field;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Dw3.Extractor.Tests;

public sealed class ActionDomainWriterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"dw3-action-{Guid.NewGuid():N}");

    [Fact]
    public void WritesClimbLikeManifestFramesAndBehaviourTable()
    {
        var frames = new[]
        {
            new ActionFrame(0, 2, 1, [255, 0, 0, 255, 0, 0, 0, 0]),
        };
        var sets = new[]
        {
            new ActionSet(0x1A, "CLIMB_UP", ActionSequenceTerminator.Loop,
                [new ActionAnimationRecord(6, 0, -16, -28)]),
        };
        var domain = new ActionDomain("climb", "m3-climb-v1", 0x3CC, 0x17, "AAA/DAT/FIELD/SPRT/P002PLYL.BIN", "aabb", 123, frames, sets);

        new DataPackWriter(_root).Write(Metadata(), context => ActionDomainWriter.Write(context, domain));

        string data = Path.Combine(_root, "data");
        Assert.True(File.Exists(Path.Combine(data, "climb", "frames", "frame-00.png")));
        using SqliteConnection connection = new DataPackReader(_root).OpenDatabase();
        Assert.Equal("climb|972|23|m3-climb-v1|AAA/DAT/FIELD/SPRT/P002PLYL.BIN|aabb|123", Row(connection,
            "SELECT domain, file_id, anim_set_entry, manifest_schema, disc_path, sha256, bytes FROM action_domains"));
        Assert.Equal("climb|26|CLIMB_UP|loop", Row(connection,
            "SELECT domain, set_id, name, terminator FROM action_sets"));
        Assert.Equal("climb|26|0|6|0|-16|-28", Row(connection,
            "SELECT domain, set_id, ordinal, duration, frame_id, offset_x, offset_y FROM action_records"));

        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(data, "climb", "manifest.json")));
        Assert.Equal("m3-climb-v1", manifest.RootElement.GetProperty("schema").GetString());
        Assert.Equal("CLIMB_UP", manifest.RootElement.GetProperty("sets")[0].GetProperty("name").GetString());
        Assert.Equal(2, manifest.RootElement.GetProperty("frames").GetProperty("0").GetProperty("size")[0].GetInt32());
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            // Release pooled connections before deletion; on Windows, the pool keeps
            // the file handle open past Dispose(), preventing Directory.Delete.
            SqliteConnection.ClearAllPools();
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

    private static DataPackMetadata Metadata() => new("test", "0123456789012345678901234567890123456789", "synthetic", DateTimeOffset.Parse("2026-01-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
}
