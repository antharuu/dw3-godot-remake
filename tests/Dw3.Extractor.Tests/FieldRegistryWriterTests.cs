using Dw3.Data;
using Dw3.Formats.Field;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Dw3.Extractor.Tests;

public sealed class FieldRegistryWriterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"dw3-field-registry-{Guid.NewGuid():N}");

    [Fact]
    public void WritePersistsStagesAndResolvesExitDestinationsByDiscMode()
    {
        var stages = new[]
        {
            new ExtractedFieldStage(200, 0x200, 17, "loadable", null,
                [new StageSlot(ushort.MaxValue, 1, 0x201, 12, 34, 5)]),
            new ExtractedFieldStage(202, 0x201, 23, "error", "missing terrain packet", []),
        };
        var writer = new DataPackWriter(_root);
        writer.Write(Metadata(), context => FieldRegistryWriter.Write(context, stages));

        using SqliteConnection connection = new DataPackReader(_root).OpenDatabase();
        using SqliteCommand stageCommand = connection.CreateCommand();
        stageCommand.CommandText = "SELECT stage_id, status, error FROM field_stages ORDER BY stage_id";
        using SqliteDataReader stageRows = stageCommand.ExecuteReader();
        Assert.True(stageRows.Read());
        Assert.Equal(200, stageRows.GetInt32(0));
        Assert.Equal("loadable", stageRows.GetString(1));
        Assert.True(stageRows.IsDBNull(2));
        Assert.True(stageRows.Read());
        Assert.Equal(202, stageRows.GetInt32(0));
        Assert.Equal("error", stageRows.GetString(1));
        Assert.Equal("missing terrain packet", stageRows.GetString(2));
        Assert.False(stageRows.Read());
        stageRows.Close();

        using SqliteCommand warpCommand = connection.CreateCommand();
        warpCommand.CommandText = "SELECT source_stage_id, slot_index, destination_mode, destination_stage_id, x, y, facing, condition_status FROM field_warps";
        using SqliteDataReader warp = warpCommand.ExecuteReader();
        Assert.True(warp.Read());
        Assert.Equal(200, warp.GetInt32(0));
        Assert.Equal(0, warp.GetInt32(1));
        Assert.Equal(0x201, warp.GetInt32(2));
        Assert.Equal(202, warp.GetInt32(3));
        Assert.Equal(12, warp.GetInt32(4));
        Assert.Equal(34, warp.GetInt32(5));
        Assert.Equal(5, warp.GetInt32(6));
        Assert.Equal("unconditional", warp.GetString(7));
        Assert.False(warp.Read());
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            // Release pooled connections before deletion; on Windows, the pool keeps
            // the file handle open past Dispose(), preventing Directory.Delete.
            SqliteConnection.ClearAllPools();
        Directory.Delete(_root, recursive: true);
    }

    private static DataPackMetadata Metadata() => new(
        "test", "0123456789012345678901234567890123456789", "synthetic", DateTimeOffset.Parse("2026-01-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
}
