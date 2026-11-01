using System.Globalization;
using System.Text.Json;
using Dw3.Data;
using Dw3.Formats.Encounter;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Dw3.Extractor.Tests;

/// <summary>
/// Writes a fabricated encounter domain through the SQL and JSON writers and reads both back.
/// No game-derived bytes are used.
/// </summary>
public sealed class EncounterWritersTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"dw3-encounter-{Guid.NewGuid():N}");

    [Fact]
    public void SqlWriterPersistsEveryEncounterTableInSourceOrder()
    {
        EncounterDataPack pack = FabricatedPack();

        new DataPackWriter(_root).Write(Metadata(), context =>
            EncounterSqlWriter.Write(context, pack.Enemies, pack.Encounters, pack.BattleRates, pack.Stages));

        using SqliteConnection connection = new DataPackReader(_root).OpenDatabase();
        Assert.Equal(
            ["0|7|3|40|12|9", "1|8|5|-1|0|32767"],
            Rows(connection, "SELECT enemy_index, fighter, level, hp, mp, strength FROM encounter_enemies ORDER BY enemy_index"));
        Assert.Equal(
            ["4|0|1|-1|25|2|[1,2,255]"],
            Rows(connection, "SELECT encounter_index, enemy0, enemy1, enemy2, ambush_chance, kind, blocks FROM encounter_records"));
        Assert.Equal(
            ["0|10", "1|0", "2|99"],
            Rows(connection, "SELECT rate_index, rate FROM encounter_battle_rates ORDER BY rate_index"));
        Assert.Equal(
            ["200|0|-2147418112|1|ab12", "200|1|16|0|ab12", "235|0|32|0|cd34"],
            Rows(connection, "SELECT stage_id, array_index, addr, entry_count, overlay_sha256 FROM encounter_stage_arrays ORDER BY stage_id, array_index"));
    }

    [Fact]
    public void JsonWriterKeepsTheManifestShapeAndSortsStages()
    {
        EncounterDataPack pack = FabricatedPack();

        new DataPackWriter(_root).Write(Metadata(), context =>
        {
            EncounterJsonWriter.Write(context, pack.Enemies, pack.Encounters, pack.BattleRates, pack.Stages, pack.FieldStgIsoPath, pack.FieldStgSha256);
            context.RegisterImage("encounter/encounter.json");
        });

        string json = File.ReadAllText(Path.Combine(_root, "data", "encounter", "encounter.json"));
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        Assert.Equal(
            ["battleRates", "edition", "enemies", "encounters", "schema", "source", "stages"],
            root.EnumerateObject().Select(property => property.Name));
        Assert.Equal("m6-encounter-v1", root.GetProperty("schema").GetString());
        Assert.Equal("SLES-03936", root.GetProperty("edition").GetString());
        Assert.Equal([10, 0, 99], root.GetProperty("battleRates").EnumerateArray().Select(rate => rate.GetInt32()));
        Assert.Equal(32767, root.GetProperty("enemies")[1].GetProperty("strength").GetInt32());

        JsonElement encounter = root.GetProperty("encounters")[0];
        Assert.Equal([1, 2, 255], encounter.GetProperty("blocks").EnumerateArray().Select(block => block.GetInt32()));
        Assert.Equal(8, encounter.GetProperty("enemies")[1].GetProperty("fighter").GetInt32());
        Assert.Equal(-1, encounter.GetProperty("enemyIndices")[2].GetInt32());

        JsonElement source = root.GetProperty("source");
        Assert.Equal("FAKE/FIELDSTG.PRO", source.GetProperty("fieldstg").GetString());
        Assert.Equal(1, source.GetProperty("encounter_count").GetInt32());
        Assert.Equal(2, source.GetProperty("enemies_count").GetInt32());

        JsonElement stages = root.GetProperty("stages");
        Assert.Equal(["200", "235"], stages.EnumerateObject().Select(stage => stage.Name));
        JsonElement battle = stages.GetProperty("200").GetProperty("stageBattles")[0];
        Assert.Equal(unchecked((int)0x80010000u), battle.GetProperty("addr").GetInt32());
        JsonElement place = battle.GetProperty("places")[0];
        Assert.Equal(3, place.GetProperty("serial").GetInt32());
        JsonElement area = place.GetProperty("areas")[0];
        Assert.Equal(1, area.GetProperty("count").GetInt32());
        Assert.Equal("fake-theme", area.GetProperty("battles")[0].GetProperty("musicName").GetString());
        Assert.Equal("cd34", stages.GetProperty("235").GetProperty("overlay_sha256").GetString());
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static EncounterDataPack FabricatedPack()
    {
        var weak = new BattleEnemy(0, 7, 3, 40, 12, 9);
        var strong = new BattleEnemy(1, 8, 5, -1, 0, short.MaxValue);
        var encounter = new EncounterRecord(
            4,
            [0, 1, -1],
            [new EncounterEnemySlot(0, weak), new EncounterEnemySlot(1, strong)],
            25,
            2,
            [1, 2, 255]);
        var place = new FieldBattles(3, 9, 0, [new BattleList(1, [new AreaBattle(4, 12, 6, "fake-theme")])]);
        var stages = new Dictionary<int, EncounterStageData>
        {
            [235] = new("FAKE235.BIN", "cd34", [new StageBattleArray(32, 0, [])]),
            [200] = new("FAKE200.BIN", "ab12", [new StageBattleArray(0x80010000u, 1, [place]), new StageBattleArray(16, 0, [])]),
        };
        return new EncounterDataPack([weak, strong], [encounter], [10, 0, 99], stages, "FAKE/FIELDSTG.PRO", "ff00");
    }

    private static List<string> Rows(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        using SqliteDataReader reader = command.ExecuteReader();
        var rows = new List<string>();
        while (reader.Read())
        {
            rows.Add(string.Join('|', Enumerable.Range(0, reader.FieldCount)
                .Select(index => Convert.ToString(reader.GetValue(index), CultureInfo.InvariantCulture))));
        }

        return rows;
    }

    private static DataPackMetadata Metadata() => new("test", new string('0', 40), "test-edition", DateTimeOffset.UnixEpoch);
}
