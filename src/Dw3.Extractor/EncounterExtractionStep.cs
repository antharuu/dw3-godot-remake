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

    /// <summary>
    /// Reads FIELDSTG.PRO and all WSTAG overlays, decodes enemies, encounters, battle rates, and
    /// per-stage battle arrays, then writes them to <paramref name="outputDirectory"/>.
    /// </summary>
    /// <param name="cuePath">Path to the CUE sheet of the supported disc image.</param>
    /// <param name="outputDirectory">Root of the data pack to write into.</param>
    /// <param name="output">Receives one JSON completion event line on success.</param>
    /// <param name="error">Receives one plain-text error line on failure.</param>
    /// <returns>0 on success, 1 on extraction error.</returns>
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
            Dictionary<int, EncounterStageData> stages = ScanStageArrays(disc, entries);
            var data = new EncounterDataPack(
                enemies,
                encounters,
                battleRates,
                stages,
                fieldStgEntry.IsoPath,
                Convert.ToHexStringLower(SHA256.HashData(fieldStg)));
            WriteDataPack(outputDirectory, edition, data);
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

    /// <summary>
    /// Returns the FIELDSTG.PRO disc entry, throwing <see cref="InvalidDataException"/> when absent.
    /// </summary>
    /// <param name="entries">The full disc file entry list to search.</param>
    /// <returns>The FIELDSTG.PRO disc file entry.</returns>
    /// <exception cref="InvalidDataException">FIELDSTG.PRO is not present on the disc.</exception>
    private static DiscFileEntry FindFieldStageEntry(IReadOnlyList<DiscFileEntry> entries)
    {
        return entries.SingleOrDefault(entry => string.Equals(entry.IsoPath, FieldStgPath, StringComparison.Ordinal))
            ?? throw new InvalidDataException($"Required disc member missing: {FieldStgPath}");
    }

    /// <summary>
    /// Iterates all WSTAG stage overlays in the disc entry list and decodes the battle array
    /// for each stage whose overlay contains the field-battle-array pattern.
    /// </summary>
    /// <param name="disc">The open disc source for reading overlay bytes.</param>
    /// <param name="entries">The full disc file entry list.</param>
    /// <returns>A map from stage id to decoded stage battle arrays.</returns>
    private static Dictionary<int, EncounterStageData> ScanStageArrays(
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

    /// <summary>
    /// Returns all disc file entries whose ISO path matches the WSTAG overlay pattern
    /// (<c>AAA/PRO/WSTAG*.PRO</c>), ordered by path.
    /// </summary>
    /// <param name="entries">The full disc file entry list.</param>
    /// <returns>An ordered sequence of WSTAG overlay entries.</returns>
    private static IOrderedEnumerable<DiscFileEntry> FindStageOverlays(IReadOnlyList<DiscFileEntry> entries)
    {
        return entries
            .Where(entry => entry.IsoPath.StartsWith("AAA/PRO/WSTAG", StringComparison.Ordinal)
                && entry.IsoPath.EndsWith(".PRO", StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => entry.IsoPath, StringComparer.Ordinal);
    }

    /// <summary>
    /// Scans a single WSTAG overlay for battle arrays and adds the result to <paramref name="stages"/>
    /// when at least one array is found. Overlays with no arrays are silently skipped.
    /// </summary>
    /// <param name="disc">The open disc source for reading the overlay bytes.</param>
    /// <param name="entry">The disc file entry for the WSTAG overlay.</param>
    /// <param name="stages">The mutable map to add the decoded stage data to.</param>
    private static void TryAddStageArrays(
        DiscSource disc,
        DiscFileEntry entry,
        Dictionary<int, EncounterStageData> stages)
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

    /// <summary>
    /// Parses the numeric stage id from a WSTAG filename stem (e.g. <c>WSTAG200</c> → 200).
    /// Returns <see langword="false"/> when the stem is too short to contain the id digits.
    /// </summary>
    /// <param name="stem">The filename stem (the filename without extension).</param>
    /// <param name="stageId">Receives the parsed stage id, or 0 on failure.</param>
    /// <returns><see langword="true"/> when the id was parsed successfully.</returns>
    private static bool TryReadStageId(string stem, out int stageId)
    {
        if (stem.Length <= 5)
        {
            stageId = 0;
            return false;
        }

        return int.TryParse(stem.AsSpan(5), out stageId);
    }

    /// <summary>
    /// Writes the encounter data pack to <paramref name="outputDirectory"/> using both SQLite and JSON writers.
    /// </summary>
    /// <param name="outputDirectory">Root of the data pack to write into.</param>
    /// <param name="edition">The disc edition metadata written into the pack header.</param>
    /// <param name="data">The fully decoded encounter data including enemies, encounters, rates, and stages.</param>
    private static void WriteDataPack(string outputDirectory, SupportedEdition edition, EncounterDataPack data)
    {
        new DataPackWriter(outputDirectory).Write(
            new DataPackMetadata(
                typeof(EncounterExtractionStep).Assembly.GetName().Version?.ToString(3) ?? "0.1.0",
                edition.BinSha1,
                edition.Label,
                DateTimeOffset.UtcNow),
            context =>
            {
                EncounterSqlWriter.Write(context, data.Enemies, data.Encounters, data.BattleRates, data.Stages);
                EncounterJsonWriter.Write(context, data.Enemies, data.Encounters, data.BattleRates, data.Stages, data.FieldStgIsoPath, data.FieldStgSha256);
            });
    }

    /// <summary>
    /// Writes the domain-complete JSON event line to <paramref name="output"/> with enemy, encounter,
    /// and stage counts.
    /// </summary>
    /// <param name="output">Receives the completion event line.</param>
    /// <param name="enemyCount">The total number of decoded enemy records.</param>
    /// <param name="encounterCount">The total number of decoded encounter records.</param>
    /// <param name="stageCount">The number of stages that contained at least one battle array.</param>
    private static void WriteCompletion(TextWriter output, int enemyCount, int encounterCount, int stageCount)
    {
        output.WriteLine(
            $"{{\"event\":\"domain-complete\",\"domain\":\"{Domain}\"," +
            $"\"enemies\":{enemyCount},\"encounters\":{encounterCount},\"stages\":{stageCount}}}");
    }
}
