using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Dw3.Data;
using Dw3.Disc;
using Dw3.Formats.Encounter;
using Microsoft.Data.Sqlite;

namespace Dw3.Extractor;

/// <summary>
/// Extracts the encounter domain from the supported disc:
///   enemies, encounters, and battle rates from FIELDSTG.PRO (overlay at 0x80082CB0);
///   per-stage FieldBattles arrays from each WSTAG*.PRO that has them (overlays at 0x800A5DE0).
///
/// Stage battle discovery: each stage setup function stores its stageBattles array pointer to
/// FIELDSTG_state.battles (FIELDSTG_state = 0x80099D80, battles field offset = 0x20). StageBattlesScanner
/// uses MipsStoreScanner to find these stores and structural validation to count entries.
///
/// Outputs:
///   SQLite tables: encounter_enemies, encounter_records, encounter_battle_rates, encounter_stage_arrays.
///   JSON file:     encounter/encounter.json — used by the parity harness.
/// </summary>
internal static class EncounterExtractionStep
{
    private const string Domain = "encounter";
    private const string FieldStgPath = "AAA/PRO/FIELDSTG.PRO";
    private const string SchemaLabel = "m6-encounter-v1";
    private const string EditionLabel = "SLES-03936";

    private sealed record StageData(
        string OverlayName,
        string OverlaySha256,
        IReadOnlyList<StageBattleArray> Arrays);

    internal static int Extract(string cuePath, string outputDirectory, TextWriter output, TextWriter error)
    {
        try
        {
            using var disc = DiscSource.Open(cuePath);
            SupportedEdition edition = disc.IdentifyEdition();
            IReadOnlyList<DiscFileEntry> entries = disc.ListFiles();

            // ── Read FIELDSTG.PRO ─────────────────────────────────────────────
            DiscFileEntry fieldStgEntry = entries.SingleOrDefault(e =>
                string.Equals(e.IsoPath, FieldStgPath, StringComparison.Ordinal))
                ?? throw new InvalidDataException($"Required disc member missing: {FieldStgPath}");
            byte[] fieldStg = disc.ReadFile(fieldStgEntry);
            string fieldStgSha256 = Convert.ToHexStringLower(SHA256.HashData(fieldStg));

            IReadOnlyList<BattleEnemy> enemies = FieldEncounterDecoder.DecodeEnemies(fieldStg);
            IReadOnlyList<EncounterRecord> encounters = FieldEncounterDecoder.DecodeEncounters(fieldStg, enemies);
            IReadOnlyList<int> battleRates = FieldEncounterDecoder.DecodeBattleRates(fieldStg);

            // ── Discover stageBattles in every WSTAG*.PRO ─────────────────────
            var stages = new Dictionary<int, StageData>();
            foreach (DiscFileEntry entry in entries
                .Where(e => e.IsoPath.StartsWith("AAA/PRO/WSTAG", StringComparison.Ordinal)
                         && e.IsoPath.EndsWith(".PRO", StringComparison.OrdinalIgnoreCase))
                .OrderBy(e => e.IsoPath, StringComparer.Ordinal))
            {
                string baseName = Path.GetFileName(entry.IsoPath);
                string stem = Path.GetFileNameWithoutExtension(baseName);
                if (stem.Length <= 5 || !int.TryParse(stem.AsSpan(5), out int stageId))
                    continue;
                byte[] overlay = disc.ReadFile(entry);
                IReadOnlyList<StageBattleArray> arrays = StageBattlesScanner.Scan(
                    overlay,
                    FieldEncounterDecoder.StageOverlayBase,
                    FieldEncounterDecoder.FieldStateAddress,
                    FieldEncounterDecoder.BattlesFieldOffset);
                if (arrays.Count > 0)
                {
                    string overlaySha256 = Convert.ToHexStringLower(SHA256.HashData(overlay));
                    stages[stageId] = new StageData(baseName, overlaySha256, arrays);
                }
            }

            // ── Write to data pack ────────────────────────────────────────────
            new DataPackWriter(outputDirectory).Write(
                new DataPackMetadata(
                    typeof(EncounterExtractionStep).Assembly.GetName().Version?.ToString(3) ?? "0.1.0",
                    edition.BinSha1,
                    edition.Label,
                    DateTimeOffset.UtcNow),
                context =>
                {
                    WriteToDatabase(context, enemies, encounters, battleRates, stages);
                    WriteJson(context, enemies, encounters, battleRates, stages,
                        fieldStgEntry.IsoPath, fieldStgSha256);
                });

            output.WriteLine(
                $"{{\"event\":\"domain-complete\",\"domain\":\"{Domain}\"," +
                $"\"enemies\":{enemies.Count},\"encounters\":{encounters.Count}," +
                $"\"stages\":{stages.Count}}}");
            return 0;
        }
        catch (Exception ex) when (ex is DiscSourceException or InvalidDataException or FormatException
                                       or IOException or ArgumentException)
        {
            error.WriteLine($"Extraction error [{Domain}]: {ex.Message}");
            return 1;
        }
    }

    // ── Database writes ───────────────────────────────────────────────────────

    private static void WriteToDatabase(
        DataPackWriteContext context,
        IReadOnlyList<BattleEnemy> enemies,
        IReadOnlyList<EncounterRecord> encounters,
        IReadOnlyList<int> battleRates,
        Dictionary<int, StageData> stages)
    {
        foreach (BattleEnemy enemy in enemies)
            Insert(context,
                "INSERT INTO encounter_enemies (enemy_index,fighter,level,hp,mp,strength) VALUES ($1,$2,$3,$4,$5,$6)",
                enemy.Index, (int)enemy.Fighter, (int)enemy.Level, (int)enemy.Hp, (int)enemy.Mp, (int)enemy.Strength);

        foreach (EncounterRecord enc in encounters)
        {
            string blocksJson = "[" + string.Join(",", enc.Blocks) + "]";
            Insert(context,
                "INSERT INTO encounter_records (encounter_index,enemy0,enemy1,enemy2,ambush_chance,kind,blocks) VALUES ($1,$2,$3,$4,$5,$6,$7)",
                enc.Index, enc.EnemyIndices[0], enc.EnemyIndices[1], enc.EnemyIndices[2],
                (int)enc.AmbushChance, (int)enc.Kind, blocksJson);
        }

        for (int i = 0; i < battleRates.Count; i++)
            Insert(context,
                "INSERT INTO encounter_battle_rates (rate_index,rate) VALUES ($1,$2)",
                i, battleRates[i]);

        foreach ((int stageId, StageData stageData) in stages)
        {
            for (int ai = 0; ai < stageData.Arrays.Count; ai++)
            {
                StageBattleArray arr = stageData.Arrays[ai];
                int addrSigned = unchecked((int)arr.Address);
                Insert(context,
                    "INSERT INTO encounter_stage_arrays (stage_id,array_index,addr,entry_count,overlay_sha256) VALUES ($1,$2,$3,$4,$5)",
                    stageId, ai, addrSigned, arr.Count, stageData.OverlaySha256);
            }
        }
    }

    private static void Insert(DataPackWriteContext context, string sql, params object[] values)
    {
        using SqliteCommand command = context.Database.CreateCommand();
        command.Transaction = context.Transaction;
        command.CommandText = sql;
        for (int index = 0; index < values.Length; index++)
            command.Parameters.AddWithValue($"${index + 1}", values[index]);
        command.ExecuteNonQuery();
    }

    // ── JSON output (for parity) ──────────────────────────────────────────────

    private static void WriteJson(
        DataPackWriteContext context,
        IReadOnlyList<BattleEnemy> enemies,
        IReadOnlyList<EncounterRecord> encounters,
        IReadOnlyList<int> battleRates,
        Dictionary<int, StageData> stages,
        string fieldStgIsoPath,
        string fieldStgSha256)
    {
        string jsonPath = context.GetImagePath("encounter/encounter.json");
        Directory.CreateDirectory(Path.GetDirectoryName(jsonPath)!);

        var jOptions = new JsonWriterOptions { Indented = true };
        using var stream = new FileStream(jsonPath, FileMode.Create, FileAccess.Write);
        using var writer = new Utf8JsonWriter(stream, jOptions);
        writer.WriteStartObject();

        // battleRates
        writer.WriteStartArray("battleRates");
        foreach (int rate in battleRates) writer.WriteNumberValue(rate);
        writer.WriteEndArray();

        // edition
        writer.WriteString("edition", EditionLabel);

        // enemies
        writer.WriteStartArray("enemies");
        foreach (BattleEnemy e in enemies)
        {
            writer.WriteStartObject();
            writer.WriteNumber("fighter", e.Fighter);
            writer.WriteNumber("hp", e.Hp);
            writer.WriteNumber("index", e.Index);
            writer.WriteNumber("level", e.Level);
            writer.WriteNumber("mp", e.Mp);
            writer.WriteNumber("strength", e.Strength);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();

        // encounters
        writer.WriteStartArray("encounters");
        foreach (EncounterRecord enc in encounters)
        {
            writer.WriteStartObject();
            writer.WriteNumber("ambushChance", enc.AmbushChance);
            writer.WriteStartArray("blocks");
            foreach (byte b in enc.Blocks) writer.WriteNumberValue(b);
            writer.WriteEndArray();
            writer.WriteStartArray("enemies");
            foreach (EncounterEnemySlot slot in enc.Slots)
            {
                BattleEnemy e = slot.Enemy;
                writer.WriteStartObject();
                writer.WriteNumber("fighter", e.Fighter);
                writer.WriteNumber("hp", e.Hp);
                writer.WriteNumber("index", e.Index);
                writer.WriteNumber("level", e.Level);
                writer.WriteNumber("mp", e.Mp);
                writer.WriteNumber("strength", e.Strength);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteStartArray("enemyIndices");
            foreach (int idx in enc.EnemyIndices) writer.WriteNumberValue(idx);
            writer.WriteEndArray();
            writer.WriteNumber("index", enc.Index);
            writer.WriteNumber("kind", enc.Kind);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();

        // schema
        writer.WriteString("schema", SchemaLabel);

        // source
        writer.WriteStartObject("source");
        writer.WriteNumber("battle_rates_addr", FieldEncounterDecoder.BattleRatesAddr);
        writer.WriteNumber("encounter_count", encounters.Count);
        writer.WriteNumber("encounters_addr", FieldEncounterDecoder.EncountersAddr);
        writer.WriteNumber("enemies_addr", FieldEncounterDecoder.EnemiesAddr);
        writer.WriteNumber("enemies_count", enemies.Count);
        writer.WriteString("fieldstg", fieldStgIsoPath);
        writer.WriteString("fieldstg_sha256", fieldStgSha256);
        writer.WriteEndObject();

        // stages (sorted by stage number)
        writer.WriteStartObject("stages");
        foreach ((int stageId, StageData stageData) in stages.OrderBy(kv => kv.Key))
        {
            writer.WriteStartObject(stageId.ToString(CultureInfo.InvariantCulture));
            writer.WriteString("overlay", stageData.OverlayName);
            writer.WriteString("overlay_sha256", stageData.OverlaySha256);
            writer.WriteStartArray("stageBattles");
            foreach (StageBattleArray arr in stageData.Arrays)
            {
                writer.WriteStartObject();
                // addr as signed 32-bit to stay JSON-number safe (matching Python canon).
                int addrSigned = unchecked((int)arr.Address);
                writer.WriteNumber("addr", addrSigned);
                writer.WriteStartArray("places");
                foreach (FieldBattles fb in arr.Places)
                {
                    writer.WriteStartObject();
                    writer.WriteStartArray("areas");
                    foreach (BattleList bl in fb.Areas)
                    {
                        writer.WriteStartObject();
                        writer.WriteStartArray("battles");
                        foreach (AreaBattle ab in bl.Battles)
                        {
                            writer.WriteStartObject();
                            writer.WriteNumber("encounter", ab.Encounter);
                            writer.WriteNumber("music", ab.Music);
                            writer.WriteString("musicName", ab.MusicName);
                            writer.WriteNumber("stage", ab.Stage);
                            writer.WriteEndObject();
                        }
                        writer.WriteEndArray();
                        writer.WriteNumber("count", bl.Count);
                        writer.WriteEndObject();
                    }
                    writer.WriteEndArray();
                    writer.WriteNumber("id", fb.Id);
                    writer.WriteNumber("pad8", fb.Pad8);
                    writer.WriteNumber("serial", fb.Serial);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        writer.WriteEndObject();

        writer.WriteEndObject();
    }
}
