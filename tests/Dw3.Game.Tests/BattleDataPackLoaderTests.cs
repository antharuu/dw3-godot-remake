using System.Text.Json;
using Dw3.Data;
using Dw3.Data.Battle;
using Dw3.Extractor;
using Dw3Production.Conversion;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Dw3.Game.Tests;

public sealed class BattleDataPackLoaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"dw3-battle-pack-{Guid.NewGuid():N}");

    [Fact]
    public void ResolvesValidatedModelStageAndActionManifests()
    {
        BuildPack();
        using var loader = new BattleDataPackLoader(new DataPackReader(_root));

        Assert.EndsWith("battle/models/fighter-383-model.json", loader.ModelPath(383).Replace('\\', '/'));
        Assert.EndsWith("battle/stages/stage-18-model.json", loader.StagePath(18).Replace('\\', '/'));
        Assert.EndsWith("battle/actions/fighter-383-script-5.json", loader.ActionScriptPath(383, 5).Replace('\\', '/'));
        Assert.Throws<CacheResourceException>(() => loader.ActionScriptPath(383, 11));
        Assert.Throws<CacheResourceException>(() => loader.ActionScriptPath(51, 5));
    }

    /// <summary>
    /// Verifies the full roundtrip: BattleActionDomainWriter writes a pack with one decoded,
    /// one unsupported and one absent record, then BattleDataPackLoader resolves only the decoded
    /// one. The resolved manifest file contains exactly the commands that were originally written.
    /// </summary>
    [Fact]
    public void EffectModelPathDistinguishesPreparedAndSourceAbsentRecords()
    {
        BuildPack();
        using var loader = new BattleDataPackLoader(new DataPackReader(_root));

        Assert.EndsWith("battle/effects/effect-39-model.json", loader.EffectModelPath(39)?.Replace('\\', '/'));
        Assert.Null(loader.EffectModelPath(373));
    }

    [Fact]
    public void WriterRoundtripDecoded_UnsupportedAndAbsent()
    {
        BuildPack();
        using var loader = new BattleDataPackLoader(new DataPackReader(_root));
        using SqliteConnection db = new DataPackReader(_root).OpenDatabase();

        Assert.Equal(1L, Scalar(db, "SELECT count(*) FROM battle_action_archives WHERE fighter_id=383 AND status='decoded'"));
        Assert.Equal(1L, Scalar(db, "SELECT count(*) FROM battle_action_archives WHERE fighter_id=383 AND status='unsupported'"));
        string decodedPath = loader.ActionScriptPath(383, 5);
        Assert.True(File.Exists(decodedPath), $"decoded manifest missing: {decodedPath}");

        // Verify the JSON file written by the production writer carries the exact command list.
        string json = File.ReadAllText(decodedPath);
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement cmds = doc.RootElement.GetProperty("commands");
        Assert.Equal(2, cmds.GetArrayLength());

        // Command 0: Model(0,0,7)
        Assert.Equal((int)BattleActionOpcode.Model, cmds[0].GetProperty("opcode").GetInt32());
        int[] args0 = [.. cmds[0].GetProperty("arguments").EnumerateArray().Select(e => e.GetInt32())];
        Assert.Equal([0, 0, 7], args0);

        // Command 1: End
        Assert.Equal((int)BattleActionOpcode.End, cmds[1].GetProperty("opcode").GetInt32());
        Assert.Equal(0, cmds[1].GetProperty("arguments").GetArrayLength());
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    private static void WriteFile(DataPackWriteContext context, string relative, string content)
    {
        string path = context.GetImagePath(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static object? Scalar(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    private void BuildPack()
    {
        new DataPackWriter(_root).Write(
            new DataPackMetadata("test", "synthetic", SyntheticDataPackFixture.SyntheticEdition, DateTimeOffset.UnixEpoch),
            context =>
            {
                WriteFile(context, "battle/models/fighter-383-model.json", "{}");
                WriteFile(context, "battle/stages/stage-18-model.json", "{}");
                using (SqliteCommand command = context.Database.CreateCommand())
                {
                    command.Transaction = context.Transaction;
                    command.CommandText = "INSERT INTO battle_models (fighter_id,entry_ordinal,record_index,kind,status,manifest_path) VALUES (383,0,0,0,'decoded','battle/models/fighter-383-model.json')";
                    command.ExecuteNonQuery();
                }

                WriteFile(context, "battle/effects/effect-39-model.json", "{}");
                using (SqliteCommand command = context.Database.CreateCommand())
                {
                    command.Transaction = context.Transaction;
                    command.CommandText = "INSERT INTO battle_effect_models (effect_id,model_file,model_entry,motion_file,motion_entry,texture_entry,bone_count,mesh_count,disc_path,sha256,manifest_path) VALUES (39,1,2,3,4,5,6,7,'synthetic','hash','battle/effects/effect-39-model.json')";
                    command.ExecuteNonQuery();
                }

                // Use the production domain writer — this is the roundtrip under test.
                var decoded = new BattleActionScript(
                    5,
                    [
                        new BattleActionCommand(BattleActionOpcode.Model, (short[])[0, 0, 7]),
                        new BattleActionCommand(BattleActionOpcode.End, []),
                    ]);
                var actions = new List<BattleActionExtraction>
                {
                    new(383, 1, 0, 5, decoded, null),
                    new(383, 1, 0, 11, null, "unsupported opcode 0x48"),
                };
                BattleActionDomainWriter.Write(context, actions);
            });
    }
}
