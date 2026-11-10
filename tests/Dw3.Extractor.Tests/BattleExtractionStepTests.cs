using System.Text.Json;
using Dw3.Data;
using Dw3.Formats.Battle;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Dw3.Extractor.Tests;

/// <summary>
/// Runs the battle and digivolution steps on a fabricated disc: a zero-filled executable with the
/// fixed record counts, a one-enemy SDIGIEDT table, a STFGTREP overlay with valid evolution
/// pointers and one-entry text tables.
/// </summary>
public sealed class BattleExtractionStepTests : IDisposable
{
    private const uint ReportBase = 0x80082CB0;
    private const uint EvolutionAddress = 0x80087A3C;
    private const uint EvolutionPointersAddress = 0x8008903C;
    private const uint RewardsAddress = 0x80086A88;
    private const uint ItemsAddress = 0x80041844;
    private const uint ItemEffectAddress = 0x80030000;
    private const uint WeaponEquipmentAddress = 0x80030020;
    private const uint ArmorEquipmentAddress = 0x80030040;
    private const uint AccessoryEquipmentAddress = 0x80030060;

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"dw3-battle-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void BattleDomainWritesEveryFixedTableAndTheTextTables()
    {
        FabricatedDisc disc = BuildDisc(validEvolutionPointers: true);
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = Cli.Run(["extract", "--disc", "x.cue", "--out", _root, "--only", "battle"], output, error, _ => disc.Open());

        Assert.True(exitCode == 0, error.ToString());
        Assert.Equal("{\"event\":\"domain-complete\",\"domain\":\"battle\"}" + Environment.NewLine, output.ToString());
        using SqliteConnection db = new DataPackReader(_root).OpenDatabase();
        Assert.Equal(55L, Scalar(db, "SELECT count(*) FROM battle_digimon"));
        Assert.Equal(443L, Scalar(db, "SELECT count(*) FROM battle_techniques"));
        Assert.Equal(402L, Scalar(db, "SELECT count(*) FROM battle_items"));
        Assert.Equal(3L, Scalar(db, "SELECT count(*) FROM battle_equipment"));
        Assert.Equal(335L, Scalar(db, "SELECT count(*) FROM battle_rewards"));
        Assert.Equal("7|2|9", Scalar(db, "SELECT id || '|' || item || '|' || item_chance FROM battle_enemy_table"));
        Assert.Equal("1200|2|3|500", Scalar(db, "SELECT price || '|' || flags || '|' || effect_kind || '|' || amount FROM battle_items WHERE id = 1"));
        Assert.Equal(DBNull.Value, Scalar(db, "SELECT effect_kind FROM battle_items WHERE id = 2"));
        Assert.Equal("weapon|91|6", Scalar(db, "SELECT category || '|' || attack || '|' || family FROM battle_equipment WHERE item_id = 2"));
        Assert.Equal("armor|73|8", Scalar(db, "SELECT category || '|' || defense || '|' || evasion FROM battle_equipment WHERE item_id = 3"));
        Assert.Equal("accessory|45|19", Scalar(db, "SELECT category || '|' || amount || '|' || stat FROM battle_equipment WHERE item_id = 4"));
        Assert.Equal("40|30|20", Scalar(db, "SELECT digimon_experience || '|' || experience || '|' || money FROM battle_rewards WHERE id = 0"));
        Assert.Equal(7L, Scalar(db, "SELECT count(*) FROM battle_text_tables WHERE entry_count = 1"));
        Assert.Equal(7L, Scalar(db, "SELECT count(*) FROM battle_text_tokens WHERE code = 18"));
        Assert.True(Scalar(db, "SELECT count(*) FROM battle_growth") is long rows && rows > 0);
        Assert.Equal("decoded|battle/actions/fighter-1.json", Scalar(db, "SELECT status || '|' || manifest_path FROM battle_action_archives WHERE fighter_id = 1"));
        Assert.Equal(2L, Scalar(db, "SELECT opcode_count FROM battle_action_scripts WHERE fighter_id = 1"));
        Assert.Contains("\"discPath\": \"AAA/DAT/COUNTRY/FRA/FSMFIGHT.BIN\"", File.ReadAllText(Path.Combine(_root, "data", "battle", "text", "fsmfight.json")));
        Assert.True(File.Exists(Path.Combine(_root, "data", "battle", "battle.json")));
        string battleJson = File.ReadAllText(Path.Combine(_root, "data", "battle", "battle.json"));
        using JsonDocument contract = JsonDocument.Parse(battleJson);
        JsonElement consumers = contract.RootElement.GetProperty("equipmentConsumers");
        Assert.Equal("BattleStats.WeaponFamilies -> DamageCalculator.FamilyBonus", consumers.GetProperty("weapon").GetProperty("family").GetString());
        Assert.Equal("BattleStats.Resist[7..11] -> B9-030 status resolver", consumers.GetProperty("accessory").GetProperty("statusImmunity").GetString());
        Assert.Equal("BattleStats.Counter/TripleHit/CriticalBonus -> B9-034 reaction resolver", consumers.GetProperty("accessory").GetProperty("reactionPassives").GetString());
        Assert.Equal(0, disc.OpenHandles);
    }

    [Fact]
    public void DigivolutionDomainWritesEightPartnersOfFortyFourRecords()
    {
        FabricatedDisc disc = BuildDisc(validEvolutionPointers: true);
        using var error = new StringWriter();

        int exitCode = Cli.Run(["extract", "--disc", "x.cue", "--out", _root, "--only", "digivolution"], TextWriter.Null, error, _ => disc.Open());

        Assert.True(exitCode == 0, error.ToString());
        using SqliteConnection db = new DataPackReader(_root).OpenDatabase();
        Assert.Equal(352L, Scalar(db, "SELECT count(*) FROM digivolution_records"));
        Assert.Equal(704L, Scalar(db, "SELECT count(*) FROM digivolution_needs"));
        Assert.Equal("3|5", Scalar(db, "SELECT stat || '|' || value FROM digivolution_records WHERE partner = 0 AND ordinal = 0"));
        Assert.True(File.Exists(Path.Combine(_root, "data", "digivolution", "digivolution.json")));
    }

    [Fact]
    public void DigivolutionRejectsEvolutionPointersThatAreNotContiguous()
    {
        FabricatedDisc disc = BuildDisc(validEvolutionPointers: false);
        using var error = new StringWriter();

        int exitCode = Cli.Run(["extract", "--disc", "x.cue", "--out", _root, "--only", "digivolution"], TextWriter.Null, error, _ => disc.Open());

        Assert.Equal(1, exitCode);
        Assert.Contains("Evolution pointer 0 does not reference the contiguous evolution table.", error.ToString());
        Assert.False(File.Exists(Path.Combine(_root, "data", "dw3.sqlite")));
    }

    [Fact]
    public void BattleTableWithoutTerminatorFailsTheDomain()
    {
        FabricatedDisc disc = BuildDisc(validEvolutionPointers: true, terminatedTable: false);
        using var error = new StringWriter();

        int exitCode = Cli.Run(["extract", "--disc", "x.cue", "--out", _root, "--only", "battle"], TextWriter.Null, error, _ => disc.Open());

        Assert.Equal(1, exitCode);
        Assert.Contains("Battle table has no zero-id terminator.", error.ToString());
    }

    private static FabricatedDisc BuildDisc(bool validEvolutionPointers, bool terminatedTable = true)
    {
        byte[] executable = Fabricate.Executable(0x80050000);
        int item = Fabricate.ExecutableOffset(ItemsAddress);
        Fabricate.U32(executable, item, ItemEffectAddress);
        Fabricate.U16(executable, item + 4, 1200);
        int effect = Fabricate.ExecutableOffset(ItemEffectAddress);
        executable[effect] = 2;
        executable[effect + 1] = 3;
        Fabricate.U16(executable, effect + 2, 500);
        WriteEquipmentItem(executable, 1, 2, WeaponEquipmentAddress);
        WriteEquipmentItem(executable, 2, 15, ArmorEquipmentAddress);
        WriteEquipmentItem(executable, 3, 21, AccessoryEquipmentAddress);

        int weapon = Fabricate.ExecutableOffset(WeaponEquipmentAddress);
        Fabricate.U16(executable, weapon + 10, 91);
        executable[weapon + 18] = 6;
        int armor = Fabricate.ExecutableOffset(ArmorEquipmentAddress);
        Fabricate.U16(executable, armor + 12, 73);
        executable[armor + 16] = 8;
        int accessory = Fabricate.ExecutableOffset(AccessoryEquipmentAddress);
        Fabricate.U16(executable, accessory + 6, 45);
        executable[accessory + 8] = 19;

        var report = new byte[0x7000];
        for (int partner = 0; partner < 8; partner++)
        {
            uint target = validEvolutionPointers ? EvolutionAddress + (uint)(partner * 44 * BattleDataDecoder.EvolutionStride) : 0;
            Fabricate.U32(report, (int)(EvolutionPointersAddress - ReportBase) + (partner * 4), target);
        }

        int evolution = (int)(EvolutionAddress - ReportBase);
        Fabricate.U16(report, evolution + 12, 3);
        Fabricate.U16(report, evolution + 14, 5);
        int reward = (int)(RewardsAddress - ReportBase);
        Fabricate.U32(report, reward, 40);
        Fabricate.U32(report, reward + 4, 30);
        Fabricate.U32(report, reward + 8, 20);

        var table = new byte[BattleDataDecoder.BattleTableStride * (terminatedTable ? 2 : 1)];
        Fabricate.U16(table, 0, 7);
        Fabricate.U16(table, 2, 2);
        Fabricate.U16(table, 4, 9);

        var fighterTable = new byte[0x104];
        Fabricate.U32(fighterTable, 4, 0x10);
        Fabricate.U32(fighterTable, 8, 0x40);
        Fabricate.U32(fighterTable, 12, 0x100);
        Fabricate.U16(fighterTable, 0x10, 1);
        Fabricate.U32(fighterTable, 0x40, 0x0300_0000);
        Fabricate.U32(fighterTable, 0x44, 0x0300_0000);
        Fabricate.U32(fighterTable, 0x48, 0x0300_0000);

        var disc = new FabricatedDisc()
            .Add("SLES_039.36", executable)
            .Add("AAA/PRO/SDIGIEDT.PRO", table)
            .Add("AAA/PRO/STFGTREP.PRO", report)
            .Add("AAA/DAT/FIGHT/FIGHTERS.BIN", fighterTable)
            .Add("AAA/DAT/FIGHT/ACTIONS.BIN", Fabricate.Archive([[2, 0, 0, 0, 0, 0, 7, 0, 0, 0]]));
        Fabricate.FileSector(executable, 0x1CC, disc.LbaOf("AAA/DAT/FIGHT/FIGHTERS.BIN"));
        Fabricate.FileSector(executable, 0x300, disc.LbaOf("AAA/DAT/FIGHT/ACTIONS.BIN"));
        foreach (string name in new[] { "FSMFIGHT", "FSDIGNAM", "FSSKLNAM", "FSSKLINF", "FSITMNAM", "FSITMINF", "FSFGTRPT" })
        {
            disc.Add($"AAA/DAT/COUNTRY/FRA/{name}.BIN", Fabricate.TextTable([0x12]));
        }

        return disc;
    }

    private static void WriteEquipmentItem(byte[] executable, int ordinal, byte type, uint pointer)
    {
        int item = Fabricate.ExecutableOffset(ItemsAddress) + (ordinal * BattleDataDecoder.ItemStride);
        Fabricate.U32(executable, item, pointer);
        executable[item + 9] = type;
    }

    private static object? Scalar(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }
}
