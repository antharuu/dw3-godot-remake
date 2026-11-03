using System.Text.Json;
using Dw3.Data;
using Dw3.Formats.Field;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Dw3.Extractor.Tests;

public sealed class NpcDomainWriterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"dw3-npc-{Guid.NewGuid():N}");

    [Fact]
    public void WritesDiscDerivedPlacementManifestAndTables()
    {
        var zone = new NpcExtractedZone(
            200,
            "AAA/PRO/WSTAG200.PRO",
            "abc123",
            [new NpcExtractedActor(
                0,
                21,
                3,
                512,
                768,
                4,
                24,
                false,
                true,
                [new NpcCondition(0x6004, 1)],
                new NpcFileEntry(12, 7),
                "AAA/DAT/FIELD/SPRT/NPC.BIN")]);

        new DataPackWriter(_root).Write(Metadata(), context => NpcDomainWriter.WritePlacements(context, [zone], 4));

        string manifestPath = Path.Combine(_root, "data", "npc", "manifest.json");
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        JsonElement actor = manifest.RootElement.GetProperty("zones").GetProperty("WSTAG200")
            .GetProperty("actors")[0];
        Assert.Equal("actor0", actor.GetProperty("name").GetString());
        Assert.True(actor.GetProperty("snapshot_visible").GetBoolean());
        Assert.Equal(21, actor.GetProperty("id").GetInt32());

        using SqliteConnection connection = new DataPackReader(_root).OpenDatabase();
        Assert.Equal("200|0|21|512|768|4|24|1", Row(
            connection,
            "SELECT stage_id, actor_index, actor_id, x, y, direction, width, snapshot_visible FROM npc_actors"));
        Assert.Equal("200|0|0|24580|1", Row(
            connection,
            "SELECT stage_id, actor_index, ordinal, code, value FROM npc_actor_conditions"));
    }

    [Fact]
    public void WritesSingleCharacterWithFrameAndAnimSet()
    {
        // Fabricated single-character NPC: one 2×2 frame, one anim set with two sequences.
        var frames = new[]
        {
            new NpcExtractedFrame(0, 2, 2, [255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255, 0, 0, 0, 0]),
        };
        var sequences = new[]
        {
            new NpcAnimSequence(0, NpcSequenceTerminator.Loop, [new NpcAnimRecord(3, 0, -8, -16)]),
            new NpcAnimSequence(1, NpcSequenceTerminator.End,  [new NpcAnimRecord(6, 0,  0,   0)]),
        };
        var sets = new[] { new NpcAnimSet(1, [0, 0, 0, 0, 0]) };
        var character = new NpcExtractedCharacter(
            ActorId: 0x15,
            StageId: 200,
            ActorIndex: 0,
            ManifestSchema: "m7a-npc-v1",
            ArchiveDiscPath: "AAA/DAT/FIELD/SPRT/NPC_TEST.BIN",
            ArchiveSha256: "deadbeef",
            ArchiveBytes: 512,
            ArchiveEntry: 3,
            Frames: frames,
            AnimSets: sets,
            Sequences: sequences);

        new DataPackWriter(_root).Write(Metadata(), context => NpcDomainWriter.Write(context, character));

        string data = Path.Combine(_root, "data");
        Assert.True(File.Exists(Path.Combine(data, "npc", "200", "actor-0", "frames", "frame-00.png")));

        using SqliteConnection connection = new DataPackReader(_root).OpenDatabase();

        const string characterQuery = "SELECT actor_id, stage_id, actor_index, archive_disc_path, archive_entry, frame_count "
            + "FROM npc_characters WHERE actor_id = 21";
        string charRow = Row(connection, characterQuery);
        Assert.Equal("21|200|0|AAA/DAT/FIELD/SPRT/NPC_TEST.BIN|3|1", charRow);

        string setRow = Row(
            connection,
            "SELECT actor_id, stage_id, actor_index, set_id FROM npc_anim_sets");
        Assert.Equal("21|200|0|1", setRow);

        string seqRow = Row(
            connection,
            "SELECT actor_id, stage_id, actor_index, seq_index, terminator FROM npc_sequences ORDER BY seq_index LIMIT 1");
        Assert.Equal("21|200|0|0|loop", seqRow);

        string frameRow = Row(
            connection,
            "SELECT actor_id, stage_id, actor_index, frame_id, width, height FROM npc_frames");
        Assert.Equal("21|200|0|0|2|2", frameRow);

        using JsonDocument manifest = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(data, "npc", "200", "actor-0", "manifest.json")));
        Assert.Equal("m7a-npc-v1", manifest.RootElement.GetProperty("schema").GetString());
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
        Assert.True(reader.Read(), $"Expected a row from: {sql}");
        return string.Join('|', Enumerable.Range(0, reader.FieldCount)
            .Select(i => Convert.ToString(reader.GetValue(i), System.Globalization.CultureInfo.InvariantCulture)));
    }

    private static DataPackMetadata Metadata() =>
        new(
            "test",
            "0123456789012345678901234567890123456789",
            "synthetic",
            DateTimeOffset.Parse("2026-01-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
}
