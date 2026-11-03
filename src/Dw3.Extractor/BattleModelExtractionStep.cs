using Dw3.Data;
using Dw3.Disc;
using Dw3.Formats.Battle;
using Dw3.Formats.Binary;

namespace Dw3.Extractor;

/// <summary>
/// Extracts the structure (bones and meshes) of every battle fighter model from the supported disc.
/// </summary>
/// <remarks>
/// Resolution chain, all read from the disc: the executable's file-sector table maps engine file
/// ids to disc members; file id 0x1CC is the fighter table (<see cref="FighterModelTable"/>); each
/// fighter entry names a model file id and archive entry, decoded by <see cref="BattleModelDecoder"/>.
/// A fighter whose model fails to decode is recorded as failed in the report and the extraction
/// continues; a missing executable, fighter table or file-sector table aborts the domain.
/// </remarks>
internal static class BattleModelExtractionStep
{
    private const string Domain = "battle-models";
    private const string ExecutablePath = "SLES_039.36";
    private const string EditionLabel = "SLES-03936";

    /// <summary>Engine file id of the fighter table (AAA/PRO/SMDLDATA.PRO) in the file-sector table.</summary>
    private const int FighterTableFileId = 0x1CC;

    /// <summary>
    /// Opens the disc, builds the engine file table, iterates every fighter table entry,
    /// and writes models domain to <paramref name="outputDirectory"/>.
    /// </summary>
    /// <param name="openDisc">Opens the disc whose executable file table locates the fighter model files; disposed before returning.</param>
    /// <param name="outputDirectory">Root of the data pack to write into.</param>
    /// <param name="output">Receives one JSON event line on success.</param>
    /// <param name="error">Receives per-fighter decode errors and one fatal error line on failure.</param>
    /// <returns>0 on success, 1 on fatal extraction error.</returns>
    internal static int Extract(Func<IDiscSource> openDisc, string outputDirectory, TextWriter output, TextWriter error)
    {
        try
        {
            using var disc = openDisc();
            SupportedEdition edition = disc.IdentifyEdition();
            IReadOnlyList<DiscFileEntry> entries = disc.ListFiles();
            DiscFileEntry executableEntry = entries.SingleOrDefault(entry => entry.IsoPath == ExecutablePath)
                ?? throw new InvalidDataException($"Required disc member missing: {ExecutablePath}");
            PsxExecutable executable = PsxExecutable.Parse(disc.ReadFile(executableEntry));
            var files = new EngineFileReader(disc, entries, executable);
            FighterModelTable table = FighterModelTable.Parse(files.Read(FighterTableFileId).Bytes);

            var fighters = new List<BattleModelExtraction>(table.Entries.Count);
            foreach (FighterModelEntry entry in table.Entries)
            {
                fighters.Add(ExtractFighter(table, entry, files, error));
            }

            new DataPackWriter(outputDirectory).Write(
                new DataPackMetadata(
                typeof(BattleModelExtractionStep).Assembly.GetName().Version?.ToString(3) ?? "0.1.0",
                edition.BinSha1,
                edition.Label,
                DateTimeOffset.UtcNow),
                context => BattleModelDomainWriter.Write(context, fighters, EditionLabel));
            int failed = fighters.Count(fighter => !fighter.Outcome.IsDecoded);
            output.WriteLine($"{{\"event\":\"domain-complete\",\"domain\":\"{Domain}\",\"fighters\":{fighters.Count},\"failed\":{failed}}}");
            return 0;
        }
        catch (Exception ex) when (ex is DiscSourceException or InvalidDataException or FormatException or IOException or ArgumentException)
        {
            error.WriteLine($"Extraction error [{Domain}]: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Attempts to decode the full model payload for one fighter entry; returns a failed extraction
    /// record when the model data cannot be decoded, logging the reason to <paramref name="error"/>.
    /// </summary>
    /// <param name="table">The fighter model table providing reference resolution and metadata.</param>
    /// <param name="entry">The fighter table entry to extract.</param>
    /// <param name="files">Engine file reader backed by the disc's file-sector table.</param>
    /// <param name="error">Receives a single plain-text error line when decoding fails.</param>
    /// <returns>A decoded extraction or a failed extraction record with the reason.</returns>
    private static BattleModelExtraction ExtractFighter(FighterModelTable table, FighterModelEntry entry, EngineFileReader files, TextWriter error)
    {
        try
        {
            FighterModelReference reference = table.Resolve(entry.FighterId);
            (string path, byte[] bytes) = files.Read(reference.ModelFile);
            byte[] motionBytes = reference.MotionFile == reference.ModelFile ? bytes : files.Read(reference.MotionFile).Bytes;
            BattleModelPayload payload = BattleModelDecoder.DecodeComplete(
                bytes, reference.ModelEntry, motionBytes, reference.MotionEntry, table.ReadMetadata(entry.FighterId));
            return new BattleModelExtraction(BattleModelOutcome.Decoded(reference), payload.Structure, path, DiscIdentity.Sha256Bytes(bytes), payload);
        }
        catch (FormatException ex)
        {
            error.WriteLine($"ERROR battle-model-failed fighter={entry.FighterId} reason={ex.Message}");
            return new BattleModelExtraction(BattleModelOutcome.Failed(entry, ex.Message), null, null, null);
        }
    }
}
