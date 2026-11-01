using System.Security.Cryptography;
using Dw3.Data;
using Dw3.Disc;
using Dw3.Formats.Encounter;

namespace Dw3.Extractor;

/// <summary>
/// Extracts the encounter domain from FIELDSTG.PRO and stage overlays on the supported disc.
/// </summary>
internal static class EncounterExtractionStep
{
    private const string Domain = "encounter";
    private const string FieldStgPath = "AAA/PRO/FIELDSTG.PRO";

    internal static int Extract(string cuePath, string outputDirectory, TextWriter output, TextWriter error)
    {
        try
        {
            using var disc = DiscSource.Open(cuePath);
            SupportedEdition edition = disc.IdentifyEdition();
            IReadOnlyList<DiscFileEntry> entries = disc.ListFiles();
            DiscFileEntry fieldStgEntry = FindFieldStageEntry(entries);
            byte[] fieldStg = disc.ReadFile(fieldStgEntry);
            IReadOnlyList<BattleEnemy> enemies = FieldEncounterDecoder.DecodeEnemies(fieldStg);
            IReadOnlyList<EncounterRecord> encounters = FieldEncounterDecoder.DecodeEncounters(fieldStg, enemies);
            IReadOnlyList<int> battleRates = FieldEncounterDecoder.DecodeBattleRates(fieldStg);
            IReadOnlyDictionary<int, EncounterStageData> stages = ScanStageArrays(disc, entries);
            WriteDataPack(
                outputDirectory,
                edition,
                enemies,
                encounters,
                battleRates,
                stages,
                fieldStgEntry.IsoPath,
                Convert.ToHexStringLower(SHA256.HashData(fieldStg)));
            WriteCompletion(output, enemies.Count, encounters.Count, stages.Count);
            return 0;
        }
        catch (Exception ex) when (ex is DiscSourceException or InvalidDataException or FormatException
                                       or IOException or ArgumentException)
        {
            error.WriteLine($"Extraction error [{Domain}]: {ex.Message}");
            return 1;
        }
    }

    private static DiscFileEntry FindFieldStageEntry(IReadOnlyList<DiscFileEntry> entries)
    {
        return entries.SingleOrDefault(entry => string.Equals(entry.IsoPath, FieldStgPath, StringComparison.Ordinal))
            ?? throw new InvalidDataException($"Required disc member missing: {FieldStgPath}");
    }

    private static IReadOnlyDictionary<int, EncounterStageData> ScanStageArrays(
        DiscSource disc,
        IReadOnlyList<DiscFileEntry> entries)
    {
        var stages = new Dictionary<int, EncounterStageData>();
        foreach (DiscFileEntry entry in FindStageOverlays(entries))
        {
            TryAddStageArrays(disc, entry, stages);
        }

        return stages;
    }

    private static IOrderedEnumerable<DiscFileEntry> FindStageOverlays(IReadOnlyList<DiscFileEntry> entries)
    {
        return entries
            .Where(entry => entry.IsoPath.StartsWith("AAA/PRO/WSTAG", StringComparison.Ordinal)
                && entry.IsoPath.EndsWith(".PRO", StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => entry.IsoPath, StringComparer.Ordinal);
    }

    private static void TryAddStageArrays(
        DiscSource disc,
        DiscFileEntry entry,
        IDictionary<int, EncounterStageData> stages)
    {
        string baseName = Path.GetFileName(entry.IsoPath);
        string stem = Path.GetFileNameWithoutExtension(baseName);
        if (!TryReadStageId(stem, out int stageId))
        {
            return;
        }

        byte[] overlay = disc.ReadFile(entry);
        IReadOnlyList<StageBattleArray> arrays = StageBattlesScanner.Scan(
            overlay,
            FieldEncounterDecoder.StageOverlayBase,
            FieldEncounterDecoder.FieldStateAddress,
            FieldEncounterDecoder.BattlesFieldOffset);
        if (arrays.Count > 0)
        {
            stages[stageId] = new EncounterStageData(
                baseName,
                Convert.ToHexStringLower(SHA256.HashData(overlay)),
                arrays);
        }
    }

    private static bool TryReadStageId(string stem, out int stageId)
    {
        if (stem.Length <= 5)
        {
            stageId = 0;
            return false;
        }

        return int.TryParse(stem.AsSpan(5), out stageId);
    }

    private static void WriteDataPack(
        string outputDirectory,
        SupportedEdition edition,
        IReadOnlyList<BattleEnemy> enemies,
        IReadOnlyList<EncounterRecord> encounters,
        IReadOnlyList<int> battleRates,
        IReadOnlyDictionary<int, EncounterStageData> stages,
        string fieldStgIsoPath,
        string fieldStgSha256)
    {
        new DataPackWriter(outputDirectory).Write(
            new DataPackMetadata(
                typeof(EncounterExtractionStep).Assembly.GetName().Version?.ToString(3) ?? "0.1.0",
                edition.BinSha1,
                edition.Label,
                DateTimeOffset.UtcNow),
            context =>
            {
                EncounterSqlWriter.Write(context, enemies, encounters, battleRates, stages);
                EncounterJsonWriter.Write(context, enemies, encounters, battleRates, stages, fieldStgIsoPath, fieldStgSha256);
            });
    }

    private static void WriteCompletion(TextWriter output, int enemyCount, int encounterCount, int stageCount)
    {
        output.WriteLine(
            $"{{\"event\":\"domain-complete\",\"domain\":\"{Domain}\"," +
            $"\"enemies\":{enemyCount},\"encounters\":{encounterCount},\"stages\":{stageCount}}}");
    }
}
