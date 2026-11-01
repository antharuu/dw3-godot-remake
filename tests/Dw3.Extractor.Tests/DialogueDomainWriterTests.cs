using Microsoft.Data.Sqlite;
using System.Text.Json;
using Dw3.Data;
using Dw3.Extractor;
using Dw3.Formats.Dialogue;
using Dw3.Formats.Field;
using Xunit;

namespace Dw3.Extractor.Tests;

public sealed class DialogueDomainWriterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"dw3-dialogue-{Guid.NewGuid():N}");

    [Fact]
    public void WritesNumericDialogueContractAndNormalizedTables()
    {
        var progress = new ProgressConditionTables(
            [new NpcProgressRange(3, 5)], 2, new Dictionary<int, int> { [7] = 0 });
        var stage = new StageDialogueAnalysis(
            0x200, 200, new StageTextFile(197, 200, "AAA/DAT/COUNTRY/FRA/FSTALK00.BIN", 0),
            0x800A7000,
            [new StageActorDialogue(
                0, 0x800A7100,
                new NpcActorEntry(0, 0x800A7200, 21, 4, 187, 215, 1), 24,
                [
                    new FieldTalk([new NpcCondition(0x7007, 1)], [new NpcCondition(0x1A06, 1)], 12),
                    new FieldTalk([], [], 13),
                ])]);

        new DataPackWriter(_root).Write(Metadata(), context =>
            DialogueDomainWriter.Write(context, [stage], progress, snapshotProgress: 4));

        string manifestPath = Path.Combine(_root, "data", "dialogue", "manifest.json");
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        JsonElement actor = manifest.RootElement.GetProperty("zones").GetProperty("S200")
            .GetProperty("actors")[0];
        Assert.Equal(24, actor.GetProperty("width").GetInt32());
        JsonElement firstTalk = actor.GetProperty("talks")[0];
        Assert.True(firstTalk.GetProperty("selected").GetBoolean());
        Assert.Equal("special", firstTalk.GetProperty("conditions")[0].GetProperty("kind").GetString());
        Assert.Equal("flag_set", firstTalk.GetProperty("actions")[0].GetProperty("kind").GetString());
        Assert.Equal("effect", firstTalk.GetProperty("kind").GetString());
        Assert.Equal("flag_set", firstTalk.GetProperty("families")[0].GetString());

        using SqliteConnection connection = new DataPackReader(_root).OpenDatabase();
        Assert.Equal("200|0", Row(connection, "SELECT stage_id,talk_file FROM dialogue_zones"));
        Assert.Equal("200|0|0|12|1|effect", Row(connection,
            "SELECT stage_id,actor_index,talk_index,text_entry,selected,kind FROM dialogue_talks WHERE talk_index=0"));
        Assert.Equal("condition|0|28679|1|special", Row(connection,
            "SELECT list_kind,ordinal,code,value,kind FROM dialogue_codes WHERE talk_index=0 AND list_kind='condition'"));
    }

    [Fact]
    public void RejectsStageModesThatAreNotDialogueZones()
    {
        var special = new StageDialogueAnalysis(
            0x204, 212, new StageTextFile(232, 235, "AAA/DAT/COUNTRY/FRA/FSTALK05.BIN", 5), null, []);
        Assert.Throws<ArgumentException>(() => DialogueDomainWriter.ValidateDialogueStages([special]));
    }

    public void Dispose()
    {
        // Release pooled connections before deletion; on Windows, the pool keeps
        // the file handle open past Dispose(), preventing Directory.Delete.
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static string Row(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        using SqliteDataReader reader = command.ExecuteReader();
        Assert.True(reader.Read());
        return string.Join('|', Enumerable.Range(0, reader.FieldCount)
            .Select(i => Convert.ToString(reader.GetValue(i), System.Globalization.CultureInfo.InvariantCulture)));
    }

    private static DataPackMetadata Metadata() =>
        new("test", "0123456789012345678901234567890123456789", "synthetic",
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
}
