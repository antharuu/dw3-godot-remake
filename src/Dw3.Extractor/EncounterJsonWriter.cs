using System.Globalization;
using System.Text.Json;
using Dw3.Data;
using Dw3.Formats.Encounter;

namespace Dw3.Extractor;

/// <summary>
/// Writes the encounter parity manifest while retaining the established JSON property order.
/// </summary>
internal static class EncounterJsonWriter
{
    private const string EditionLabel = "SLES-03936";
    private const string SchemaLabel = "m6-encounter-v1";

    /// <summary>
    /// Writes the full encounter JSON document beneath the supplied pack context.
    /// </summary>
    /// <param name="context">Provides the destination image directory.</param>
    /// <param name="enemies">Provides decoded enemy records in source order.</param>
    /// <param name="encounters">Provides decoded encounter records in source order.</param>
    /// <param name="battleRates">Provides the battle rate table in source order.</param>
    /// <param name="stages">Maps each stage identifier to its scanned battle arrays.</param>
    /// <param name="fieldStgIsoPath">Provides the ISO path of the global encounter member.</param>
    /// <param name="fieldStgSha256">Provides the digest of the global encounter member.</param>
    internal static void Write(
        DataPackWriteContext context,
        IReadOnlyList<BattleEnemy> enemies,
        IReadOnlyList<EncounterRecord> encounters,
        IReadOnlyList<int> battleRates,
        IReadOnlyDictionary<int, EncounterStageData> stages,
        string fieldStgIsoPath,
        string fieldStgSha256)
    {
        string jsonPath = context.GetImagePath("encounter/encounter.json");
        Directory.CreateDirectory(Path.GetDirectoryName(jsonPath)!);
        using var stream = new FileStream(jsonPath, FileMode.Create, FileAccess.Write);
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        writer.WriteStartObject();
        WriteBattleRates(writer, battleRates);
        writer.WriteString("edition", EditionLabel);
        WriteEnemies(writer, enemies);
        WriteEncounters(writer, encounters);
        writer.WriteString("schema", SchemaLabel);
        WriteSource(writer, enemies, encounters, fieldStgIsoPath, fieldStgSha256);
        WriteStages(writer, stages);
        writer.WriteEndObject();
    }

    private static void WriteBattleRates(Utf8JsonWriter writer, IReadOnlyList<int> battleRates)
    {
        writer.WriteStartArray("battleRates");
        foreach (int rate in battleRates)
        {
            writer.WriteNumberValue(rate);
        }

        writer.WriteEndArray();
    }

    private static void WriteEnemies(Utf8JsonWriter writer, IReadOnlyList<BattleEnemy> enemies)
    {
        writer.WriteStartArray("enemies");
        foreach (BattleEnemy enemy in enemies)
        {
            WriteEnemy(writer, enemy);
        }

        writer.WriteEndArray();
    }

    private static void WriteEnemy(Utf8JsonWriter writer, BattleEnemy enemy)
    {
        writer.WriteStartObject();
        writer.WriteNumber("fighter", enemy.Fighter);
        writer.WriteNumber("hp", enemy.Hp);
        writer.WriteNumber("index", enemy.Index);
        writer.WriteNumber("level", enemy.Level);
        writer.WriteNumber("mp", enemy.Mp);
        writer.WriteNumber("strength", enemy.Strength);
        writer.WriteEndObject();
    }

    private static void WriteEncounters(Utf8JsonWriter writer, IReadOnlyList<EncounterRecord> encounters)
    {
        writer.WriteStartArray("encounters");
        foreach (EncounterRecord encounter in encounters)
        {
            WriteEncounter(writer, encounter);
        }

        writer.WriteEndArray();
    }

    private static void WriteEncounter(Utf8JsonWriter writer, EncounterRecord encounter)
    {
        writer.WriteStartObject();
        writer.WriteNumber("ambushChance", encounter.AmbushChance);
        WriteByteValues(writer, "blocks", encounter.Blocks);
        writer.WriteStartArray("enemies");
        foreach (EncounterEnemySlot slot in encounter.Slots)
        {
            WriteEnemy(writer, slot.Enemy);
        }

        writer.WriteEndArray();
        WriteIntValues(writer, "enemyIndices", encounter.EnemyIndices);
        writer.WriteNumber("index", encounter.Index);
        writer.WriteNumber("kind", encounter.Kind);
        writer.WriteEndObject();
    }

    private static void WriteByteValues(Utf8JsonWriter writer, string name, IReadOnlyList<byte> values)
    {
        writer.WriteStartArray(name);
        foreach (byte value in values)
        {
            writer.WriteNumberValue(value);
        }

        writer.WriteEndArray();
    }

    private static void WriteIntValues(Utf8JsonWriter writer, string name, IReadOnlyList<int> values)
    {
        writer.WriteStartArray(name);
        foreach (int value in values)
        {
            writer.WriteNumberValue(value);
        }

        writer.WriteEndArray();
    }

    private static void WriteSource(
        Utf8JsonWriter writer,
        IReadOnlyList<BattleEnemy> enemies,
        IReadOnlyList<EncounterRecord> encounters,
        string fieldStgIsoPath,
        string fieldStgSha256)
    {
        writer.WriteStartObject("source");
        writer.WriteNumber("battle_rates_addr", FieldEncounterDecoder.BattleRatesAddr);
        writer.WriteNumber("encounter_count", encounters.Count);
        writer.WriteNumber("encounters_addr", FieldEncounterDecoder.EncountersAddr);
        writer.WriteNumber("enemies_addr", FieldEncounterDecoder.EnemiesAddr);
        writer.WriteNumber("enemies_count", enemies.Count);
        writer.WriteString("fieldstg", fieldStgIsoPath);
        writer.WriteString("fieldstg_sha256", fieldStgSha256);
        writer.WriteEndObject();
    }

    private static void WriteStages(Utf8JsonWriter writer, IReadOnlyDictionary<int, EncounterStageData> stages)
    {
        writer.WriteStartObject("stages");
        foreach ((int stageId, EncounterStageData stageData) in stages.OrderBy(pair => pair.Key))
        {
            WriteStage(writer, stageId, stageData);
        }

        writer.WriteEndObject();
    }

    private static void WriteStage(Utf8JsonWriter writer, int stageId, EncounterStageData stageData)
    {
        writer.WriteStartObject(stageId.ToString(CultureInfo.InvariantCulture));
        writer.WriteString("overlay", stageData.OverlayName);
        writer.WriteString("overlay_sha256", stageData.OverlaySha256);
        writer.WriteStartArray("stageBattles");
        foreach (StageBattleArray array in stageData.Arrays)
        {
            WriteStageBattleArray(writer, array);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteStageBattleArray(Utf8JsonWriter writer, StageBattleArray array)
    {
        writer.WriteStartObject();
        writer.WriteNumber("addr", unchecked((int)array.Address));
        writer.WriteStartArray("places");
        foreach (FieldBattles place in array.Places)
        {
            WritePlace(writer, place);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WritePlace(Utf8JsonWriter writer, FieldBattles place)
    {
        writer.WriteStartObject();
        writer.WriteStartArray("areas");
        foreach (BattleList area in place.Areas)
        {
            WriteArea(writer, area);
        }

        writer.WriteEndArray();
        writer.WriteNumber("id", place.Id);
        writer.WriteNumber("pad8", place.Pad8);
        writer.WriteNumber("serial", place.Serial);
        writer.WriteEndObject();
    }

    private static void WriteArea(Utf8JsonWriter writer, BattleList area)
    {
        writer.WriteStartObject();
        writer.WriteStartArray("battles");
        foreach (AreaBattle battle in area.Battles)
        {
            writer.WriteStartObject();
            writer.WriteNumber("encounter", battle.Encounter);
            writer.WriteNumber("music", battle.Music);
            writer.WriteString("musicName", battle.MusicName);
            writer.WriteNumber("stage", battle.Stage);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteNumber("count", area.Count);
        writer.WriteEndObject();
    }
}
