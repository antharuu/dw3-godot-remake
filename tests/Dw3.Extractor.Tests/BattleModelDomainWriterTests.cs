using Microsoft.Data.Sqlite;
using System.Buffers.Binary;
using System.Text.Json;
using Dw3.Data;
using Dw3.Formats.Battle;
using Xunit;

namespace Dw3.Extractor.Tests;

public sealed class BattleModelDomainWriterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"dw3-battle-models-{Guid.NewGuid():N}");

    [Fact]
    public void WritesOneManifestPerDecodedFighterTheReportAndTheIndexTables()
    {
        var reference = new FighterModelReference(501, 1, 5, false, 12, 0, 13, 7);
        BattleModelStructure model = BattleModelDecoder.Decode(SyntheticModel(), 0);
        var fighters = new[]
        {
            new BattleModelExtraction(BattleModelOutcome.Decoded(reference), model, "AAA/DAT/TEST/MODEL.BIN", "ab12"),
            new BattleModelExtraction(BattleModelOutcome.Failed(new FighterModelEntry(77, 2, 0x40), "bone 1: broken"), null, null, null),
        };

        new DataPackWriter(_root).Write(Metadata(), context => BattleModelDomainWriter.Write(context, fighters, "TEST-EDITION"));

        string models = Path.Combine(_root, "data", "battle", "models");
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(models, "fighter-501-model.json")));
        Assert.Equal(2, manifest.RootElement.GetProperty("bone_count").GetInt32());
        Assert.False(File.Exists(Path.Combine(models, "fighter-77-model.json")));
        using JsonDocument report = JsonDocument.Parse(File.ReadAllText(Path.Combine(models, "decode-report.json")));
        Assert.Equal(1, report.RootElement.GetProperty("failed").GetInt32());

        using SqliteConnection connection = new DataPackReader(_root).OpenDatabase();
        Assert.Equal("501|0|1|5|decoded||0|12|0|13|7|1|2|1|AAA/DAT/TEST/MODEL.BIN|ab12|battle/models/fighter-501-model.json",
            Row(connection, "SELECT fighter_id, entry_ordinal, record_index, kind, status, reason, is_enemy, model_file, model_entry, motion_file, motion_entry, texture_entry, bone_count, mesh_count, disc_path, sha256, manifest_path FROM battle_models WHERE fighter_id = 501"));
        Assert.Equal("77|1|failed|bone 1: broken", Row(connection, "SELECT fighter_id, entry_ordinal, status, reason FROM battle_models WHERE fighter_id = 77"));
        Assert.Equal("501|1|0|2|4", Row(connection, "SELECT fighter_id, bone, parent, mesh_entry, key_entry FROM battle_model_bones WHERE bone = 1"));
        Assert.Equal("501|0|||", Row(connection, "SELECT fighter_id, bone, parent, mesh_entry, key_entry FROM battle_model_bones WHERE bone = 0"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            // Release pooled connections before deletion; on Windows, the pool keeps
            // the file handle open past Dispose(), preventing Directory.Delete.
            SqliteConnection.ClearAllPools();
        Directory.Delete(_root, recursive: true);
    }

    /// <summary>Archive: 0 descriptor (one non-root bone), 1 texture placeholder, 2 mesh, 3 key placeholder.</summary>
    private static byte[] SyntheticModel()
    {
        byte[] mesh = new byte[24 + 6 + 18 + 18 + 8];
        WriteU32(mesh, 0, 24);
        WriteU32(mesh, 8, 48);
        WriteU32(mesh, 20, 67);
        BinaryPrimitives.WriteInt16LittleEndian(mesh.AsSpan(24), 3);
        byte[] commands = [0x01, 0, 0, 0, 0, 0, 0, 0x91, 0x00, 0, 1, 2, 0, 0, 0, 0, 0, 0, 0xFF];
        commands.CopyTo(mesh, 48);
        byte[] descriptor = new byte[20];
        WriteU32(descriptor, 0, 1);
        WriteU32(descriptor, 4, 1);
        WriteU32(descriptor, 8, 0);
        WriteU32(descriptor, 12, 2);
        WriteU32(descriptor, 16, 4);
        byte[][] entries = [descriptor, [1, 2, 3, 4], mesh, new byte[8]];
        // Five slots: the key entry number 4 must exist in the archive table.
        var data = new List<byte>(new byte[5 * 4]);
        var table = new byte[5 * 4];
        for (int i = 0; i < entries.Length; i++)
        {
            WriteU32(table, i * 4, (uint)data.Count);
            data.AddRange(entries[i]);
        }
        WriteU32(table, 16, (uint)data.Count);
        data.AddRange(new byte[8]);
        byte[] result = [.. data];
        table.CopyTo(result, 0);
        return result;
    }

    private static void WriteU32(byte[] data, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset), value);

    private static string Row(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        using SqliteDataReader reader = command.ExecuteReader();
        Assert.True(reader.Read());
        return string.Join('|', Enumerable.Range(0, reader.FieldCount).Select(index => Convert.ToString(reader.GetValue(index), System.Globalization.CultureInfo.InvariantCulture)));
    }

    private static DataPackMetadata Metadata() => new("test", "0123456789012345678901234567890123456789", "synthetic", DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
}
