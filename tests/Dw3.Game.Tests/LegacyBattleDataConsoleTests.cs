using Xunit;

namespace Dw3.Game.Tests;

using System.Text.Json;
using Dw3.Data;
using Dw3Production.Conversion;
using Microsoft.Data.Sqlite;

public sealed class LegacyBattleDataConsoleTests
{

    [Fact]
    public void PreservesLegacyConsoleAssertions()
    {
        string root = Path.Combine(Path.GetTempPath(), $"dw3-battle-data-{Guid.NewGuid():N}");
        try
        {
            BuildSyntheticPack(root);
            using var loader = new BattleDataPackLoader(new DataPackReader(root));

            Check(loader.LoadEncounters().Encounters.Count == 1, "encounters load from pack");
            Check(loader.LoadFighters().Digimon.Count == 55, "fighters load from pack");
            Check(loader.LoadTechs().Techs.Count == 443, "techniques load from pack");
            Check(loader.LoadDigivolutions().Partners.Count == 8, "digivolutions load from pack");
            Check(loader.LoadBattleItems().Items.Count == 29, "battle items load from pack");
            Check(loader.LoadRewards().Rewards.Count == 335, "rewards load from pack");
            Check(loader.LoadBattleText().Menu.Count == 145, "battle text loads from pack");
            Check(loader.LoadBattleHud().Frames.Count == 1, "battle HUD loads from pack");
            Check(loader.LoadBattleReportSprites().Animations.Count == 8, "battle report loads from pack");
            Check(loader.LoadBattleMenuIcons().Frames.Count == 8, "battle menu icons load from pack");
            Check(File.Exists(loader.ModelPath(1)), "battle model resolves through pack index");
            Check(File.Exists(loader.StagePath(1)), "battle stage resolves through pack path");
            Check(ThrowsCacheError(() => loader.ModelPath(2), "CACHE_RESOURCE_UNAVAILABLE"), "missing model is explicit");
        }
        finally
        {
            // Release pooled connections before deletion; on Windows, the pool keeps
            // the file handle open past Dispose(), preventing Directory.Delete.
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static void BuildSyntheticPack(string root)
    {
        new DataPackWriter(root).Write(
            new DataPackMetadata("battle-data-test", "synthetic", SyntheticDataPackFixture.SyntheticEdition, DateTimeOffset.UnixEpoch),
            context =>
            {
                WriteJson(context, "encounter/encounter.json", EncounterManifest());
                WriteJson(context, "battle/battle.json", BattleManifest());
                WriteJson(context, "digivolution/digivolution.json", DigivolutionManifest());
                WriteTextTables(context);
                WriteHud(context);
                WriteReport(context);
                WriteMenuIcons(context);
                WriteFile(context, "battle/models/fighter-1-model.json", "{}");
                WriteFile(context, "battle/stages/stage-1-model.json", "{}");
                using SqliteCommand command = context.Database.CreateCommand();
                command.Transaction = context.Transaction;
                command.CommandText = "INSERT INTO battle_models (fighter_id,entry_ordinal,record_index,kind,status,manifest_path) VALUES (1,0,0,0,'decoded','battle/models/fighter-1-model.json')";
                command.ExecuteNonQuery();
            });
    }

    private static object EncounterManifest()
    {
        var battle = new { encounter = 0, stage = 1, music = 0, musicName = "synthetic" };
        var area = new { count = 1, battles = new[] { battle } };
        return new
        {
            schema = SyntheticDataPackFixture.EncounterSchema,
            // The loader verifies this protocol tag, it is not input data recovered from a disc.
            edition = SyntheticDataPackFixture.SupportedEncounterEdition,
            battleRates = new[] { 1 },
            enemies = new[] { new { index = 0, fighter = 1, level = 1, hp = 10, mp = 5, strength = 1 } },
            encounters = new[] { new { index = 0, enemyIndices = new[] { 0 }, enemies = new[] { new { fighter = 1 } }, ambushChance = 0, kind = 0, blocks = Array.Empty<int>() } },
            stages = new Dictionary<string, object>
            {
                ["1"] = new { overlay = "synthetic", overlay_sha256 = new string('0', 64), stageBattles = new[] { new { addr = 0, places = new[] { new { serial = 0, id = 0, pad8 = 0, areas = new[] { area } } } } } },
            },
        };
    }

    private static object BattleManifest()
    {
        object Digimon(int id) => new
        {
            id,
            battleStats = new int[6],
            resistances = new int[7],
            skills = new int[7],
            pairTechnique = 0,
            statusResistances = new int[5],
            skillLevels = new int[6],
            knownLevels = new int[5],
            experienceLevel = 0,
            pairPartner = 0,
            experienceRate = 0,
            hp = 1,
            mp = 1,
            hpGrowth = 0,
            mpGrowth = 0,
            statGrowth = new int[6],
            resistGrowth = new int[7],
            blastForms = new int[5],
            nameId = id,
            family = 0,
        };
        var action = new { target = 0, condition = 0, conditionArgument = 0 };
        var enemy = new { id = 0, item = 0, itemChance = 0, nameId = 0, techniques = new int[3], stats = new int[5], resistances = new int[12], family = 0, actions = new[] { action }, counter = action };
        object Technique(int id) => new { id, mp = 0, power = 1, icon = 0, kind = 0, accuracy = 100, element = 0, elementPower = 0, family = 0, effect = 0, effectChance = 0, effectPower = 0, scriptStage = 0, scriptEffect = 0, scriptSound = 0, script = 0, hitCount = 1 };
        int[] itemIds = Enumerable.Range(0x2B, 4).Concat(Enumerable.Range(0x42, 25)).ToArray();
        object Item(int id) => new { id, type = 0, price = 1, sellPrice = 0, effect = new { flags = 1, kind = 1, amount = 1 } };
        object Reward(int id) => new { id, digimonExperience = 0, experience = 0, money = 0 };
        return new
        {
            schema = SyntheticDataPackFixture.BattleSchema,
            digimon = Enumerable.Range(0, 55).Select(Digimon).ToArray(),
            enemyTable = new[] { enemy },
            techniques = Enumerable.Range(0, 443).Select(Technique).ToArray(),
            items = itemIds.Select(Item).ToArray(),
            rewards = Enumerable.Range(0, 335).Select(Reward).ToArray(),
            growth = new { levelExperience = new[] { 0 }, vitalCuts = new[] { 0 }, vitalRandom = new[] { 0 }, statGains = Enumerable.Range(0, 6).Select(_ => (object)new[] { 0 }).ToArray(), resistanceGains = new[] { 0 } },
        };
    }

    private static object DigivolutionManifest()
    {
        var need = new { digimonIndex = 0, digimonId = 0, level = 0 };
        var evolution = new { digimonIndex = 0, digimonId = 0, needs = new[] { need }, stat = 0, value = 0 };
        return new
        {
            schema = SyntheticDataPackFixture.DigivolutionSchema,
            partners = Enumerable.Range(0, 8).Select(partner => new { partner, baseId = partner, evolutions = Enumerable.Repeat(evolution, 44).ToArray() }).ToArray(),
        };
    }

    private static void WriteTextTables(DataPackWriteContext context)
    {
        WriteTextTable(context, "battle/text/fsmfight.json", 145);
        WriteTextTable(context, "battle/text/fsdignam.json", 251);
        WriteTextTable(context, "battle/text/fssklnam.json", 444);
        WriteTextTable(context, "battle/text/fssklinf.json", 220);
        WriteTextTable(context, "battle/text/fsitminf.json", 403);
        WriteTextTable(context, "battle/text/fsfgtrpt.json", 12);
        WriteTextTable(context, "battle/text/fsitmnam.json", 403);
    }

    private static void WriteTextTable(DataPackWriteContext context, string path, int count)
    {
        var token = new { kind = 0, Code = 1, Args = Array.Empty<int>() };
        WriteJson(context, path, new { entries = Enumerable.Range(0, count).Select(_ => new[] { token }).ToArray() });
    }

    private static void WriteHud(DataPackWriteContext context)
    {
        string[] rows = Enumerable.Range(0, 5).Select(index => $"frame-1-{index}.png").ToArray();
        foreach (string row in rows) WriteFile(context, $"battle/hud/{row}", "x");
        WriteFile(context, "battle/hud/cursor.png", "x");
        WriteFile(context, "battle/hud/cursor-off.png", "x");
        WriteJson(context, "battle/hud/hud.json", new
        {
            schema = SyntheticDataPackFixture.BattleHudSchema,
            frameIds = new[] { 1 },
            frames = new Dictionary<string, object> { ["1"] = new { w = 1, h = 1, offsetX = 0, offsetY = 0, clutRows = rows.Select(path => new { path }).ToArray() } },
            cursor = new { path = "cursor.png", offPath = "cursor-off.png", w = 1, h = 1, frames = 1, frameW = 1, frameH = 1 },
        });
    }

    private static void WriteReport(DataPackWriteContext context)
    {
        string[] rows = Enumerable.Range(0, 5).Select(index => $"arrow-{index}.png").ToArray();
        foreach (string row in rows) WriteFile(context, $"battle/report/{row}", "x");
        WriteFile(context, "battle/report/frame.png", "x");
        WriteJson(context, "battle/report/report.json", new
        {
            schema = SyntheticDataPackFixture.BattleReportSchema,
            frames = new Dictionary<string, object> { ["1"] = new { w = 1, h = 1, offsetX = 0, offsetY = 0, clutRows = new[] { new { path = "frame.png" } } } },
            animations = Enumerable.Range(0, 8).Select(_ => new int[7]).ToArray(),
            arrow = new { w = 1, h = 1, offsetX = 0, offsetY = 0, clutRows = rows.Select(path => new { path }).ToArray() },
        });
    }

    private static void WriteMenuIcons(DataPackWriteContext context)
    {
        int[] ids = [10, 57, 58, 59, 60, 61, 95, 97];
        var frames = new Dictionary<string, object>();
        foreach (int id in ids)
        {
            string path = $"frame-{id}.png";
            WriteFile(context, $"battle/menu-icons/{path}", "x");
            frames[id.ToString()] = new { w = 1, h = 1, offset = new[] { 0, 0 }, paths = new[] { path } };
        }
        WriteJson(context, "battle/menu-icons/manifest.json", new { schema = SyntheticDataPackFixture.BattleMenuIconSchema, frames, messageArrow = new { frame = 10, position = new[] { 0, 0 }, timeStep = 4 } });
    }

    private static void WriteJson(DataPackWriteContext context, string path, object value) =>
        WriteFile(context, path, JsonSerializer.Serialize(value));

    private static void WriteFile(DataPackWriteContext context, string path, string value)
    {
        string absolute = context.GetImagePath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
        File.WriteAllText(absolute, value);
        context.RegisterImage(path);
    }

    private static bool ThrowsCacheError(Action action, string code)
    {
        try { action(); return false; }
        catch (CacheResourceException error) { return error.Message.Contains(code, StringComparison.Ordinal); }
    }

    private static void Check(bool condition, string name)
    {
        Assert.True(condition, name);
    }
}
