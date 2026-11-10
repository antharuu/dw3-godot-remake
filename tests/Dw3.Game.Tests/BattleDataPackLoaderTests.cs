using Dw3.Data;
using Dw3.Extractor;
using Dw3.Formats.Battle;
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

    private static void Execute(DataPackWriteContext context, string sql)
    {
        using SqliteCommand command = context.Database.CreateCommand();
        command.Transaction = context.Transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
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
                Execute(context, "INSERT INTO battle_models (fighter_id,entry_ordinal,record_index,kind,status,manifest_path) VALUES (383,0,0,0,'decoded','battle/models/fighter-383-model.json')");

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
