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

    /// <summary>
    /// Writes the <c>battleRates</c> JSON array with one entry per rate in source order.
    /// </summary>
    /// <param name="writer">The JSON writer to append to.</param>
    /// <param name="battleRates">The battle rate values in source order.</param>
    private static void WriteBattleRates(Utf8JsonWriter writer, IReadOnlyList<int> battleRates)
    {
        writer.WriteStartArray("battleRates");
        foreach (int rate in battleRates)
        {
            writer.WriteNumberValue(rate);
        }

        writer.WriteEndArray();
    }

    /// <summary>Writes the <c>enemies</c> JSON array with one object per enemy in source order.</summary>
    /// <param name="writer">The JSON writer to append to.</param>
    /// <param name="enemies">The decoded enemy records in source order.</param>
    private static void WriteEnemies(Utf8JsonWriter writer, IReadOnlyList<BattleEnemy> enemies)
    {
        writer.WriteStartArray("enemies");
        foreach (BattleEnemy enemy in enemies)
        {
            WriteEnemy(writer, enemy);
        }

        writer.WriteEndArray();
    }

    /// <summary>Writes one enemy JSON object with fighter, hp, index, level, mp, and strength fields.</summary>
    /// <param name="writer">The JSON writer to append to.</param>
    /// <param name="enemy">The decoded enemy record.</param>
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

    /// <summary>Writes the <c>encounters</c> JSON array with one object per encounter in source order.</summary>
    /// <param name="writer">The JSON writer to append to.</param>
    /// <param name="encounters">The decoded encounter records in source order.</param>
    private static void WriteEncounters(Utf8JsonWriter writer, IReadOnlyList<EncounterRecord> encounters)
    {
        writer.WriteStartArray("encounters");
        foreach (EncounterRecord encounter in encounters)
        {
            WriteEncounter(writer, encounter);
        }

        writer.WriteEndArray();
    }

    /// <summary>
    /// Writes one encounter JSON object with ambush chance, blocks, enemies, enemy indices, index, and kind.
    /// </summary>
    /// <param name="writer">The JSON writer to append to.</param>
    /// <param name="encounter">The decoded encounter record.</param>
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

    /// <summary>Writes a named JSON array of byte values.</summary>
    /// <param name="writer">The JSON writer to append to.</param>
    /// <param name="name">The property name for the array.</param>
    /// <param name="values">The byte values to write.</param>
    private static void WriteByteValues(Utf8JsonWriter writer, string name, IReadOnlyList<byte> values)
    {
        writer.WriteStartArray(name);
        foreach (byte value in values)
        {
            writer.WriteNumberValue(value);
        }

        writer.WriteEndArray();
    }

    /// <summary>Writes a named JSON array of int values.</summary>
    /// <param name="writer">The JSON writer to append to.</param>
    /// <param name="name">The property name for the array.</param>
    /// <param name="values">The int values to write.</param>
    private static void WriteIntValues(Utf8JsonWriter writer, string name, IReadOnlyList<int> values)
    {
        writer.WriteStartArray(name);
        foreach (int value in values)
        {
            writer.WriteNumberValue(value);
        }

        writer.WriteEndArray();
    }

    /// <summary>
    /// Writes the <c>source</c> object with provenance fields: enemy and encounter counts,
    /// fixed RAM addresses, and the FIELDSTG.PRO ISO path and SHA-256 hash.
    /// </summary>
    /// <param name="writer">The JSON writer to append to.</param>
    /// <param name="enemies">The decoded enemy list; only its count is written.</param>
    /// <param name="encounters">The decoded encounter list; only its count is written.</param>
    /// <param name="fieldStgIsoPath">The ISO path of FIELDSTG.PRO.</param>
    /// <param name="fieldStgSha256">The SHA-256 digest of FIELDSTG.PRO.</param>
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

    /// <summary>Writes the <c>stages</c> JSON object keyed by <c>S###</c> stage name.</summary>
    /// <param name="writer">The JSON writer to append to.</param>
    /// <param name="stages">Maps stage identifiers to their decoded stage array data.</param>
    private static void WriteStages(Utf8JsonWriter writer, IReadOnlyDictionary<int, EncounterStageData> stages)
    {
        writer.WriteStartObject("stages");
        foreach ((int stageId, EncounterStageData stageData) in stages.OrderBy(pair => pair.Key))
        {
            WriteStage(writer, stageId, stageData);
        }

        writer.WriteEndObject();
    }

    /// <summary>
    /// Writes one stage JSON object with its arrays, overlay ISO filename, and SHA-256.
    /// </summary>
    /// <param name="writer">The JSON writer to append to.</param>
    /// <param name="stageId">The stage identifier used as the key in the parent object.</param>
    /// <param name="stageData">The decoded stage data with battle arrays and provenance.</param>
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

    /// <summary>
    /// Writes one battle array JSON object with RAM address, entry count, and all
    /// decoded field-battle place entries.
    /// </summary>
    /// <param name="writer">The JSON writer to append to.</param>
    /// <param name="array">The decoded stage battle array from the WSTAG overlay.</param>
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

    /// <summary>
    /// Writes one field-battle place JSON object with id, serial, pad8, and its areas array.
    /// </summary>
    /// <param name="writer">The JSON writer to append to.</param>
    /// <param name="place">The decoded field-battle place entry from the stage battle array.</param>
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

    /// <summary>
    /// Writes one battle area JSON object with rate, encounter indices, and rate-100 value.
    /// </summary>
    /// <param name="writer">The JSON writer to append to.</param>
    /// <param name="area">The decoded battle area with rate and encounter index list.</param>
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
