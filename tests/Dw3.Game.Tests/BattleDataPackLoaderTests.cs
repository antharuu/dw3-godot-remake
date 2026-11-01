using Dw3.Data;
using Dw3Production.Conversion;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Dw3.Game.Tests;

public sealed class BattleDataPackLoaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"dw3-battle-pack-{Guid.NewGuid():N}");

    [Fact]
    public void ResolvesValidatedModelAndStageManifests()
    {
        BuildPack();
        using var loader = new BattleDataPackLoader(new DataPackReader(_root));

        Assert.EndsWith("battle/models/fighter-383-model.json", loader.ModelPath(383).Replace('\\', '/'));
        Assert.EndsWith("battle/stages/stage-18-model.json", loader.StagePath(18).Replace('\\', '/'));
        Assert.Throws<CacheResourceException>(() => loader.ModelPath(51));
    }

    private void BuildPack()
    {
        new DataPackWriter(_root).Write(
            new DataPackMetadata("test", "synthetic", SyntheticDataPackFixture.SyntheticEdition, DateTimeOffset.UnixEpoch),
            context =>
            {
                Write(context, "battle/models/fighter-383-model.json", "{}");
                Write(context, "battle/stages/stage-18-model.json", "{}");
                Execute(context, "INSERT INTO battle_models (fighter_id,entry_ordinal,record_index,kind,status,manifest_path) VALUES (383,0,0,0,'decoded','battle/models/fighter-383-model.json')");
            });
    }

    private static void Write(DataPackWriteContext context, string relative, string content)
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

    public void Dispose()
    {
        // Release pooled connections before deletion; on Windows, the pool keeps
        // the file handle open past Dispose(), preventing Directory.Delete.
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
