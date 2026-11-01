using Dw3.Data;
using Dw3.Disc;
using Dw3.Formats.Battle;
using Dw3.Formats.Binary;
using Dw3.Formats.Dialogue;

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

    internal static int Extract(string cuePath, string outputDirectory, TextWriter output, TextWriter error)
    {
        try
        {
            using var disc = DiscSource.Open(cuePath);
            SupportedEdition edition = disc.IdentifyEdition();
            IReadOnlyList<DiscFileEntry> entries = disc.ListFiles();
            DiscFileEntry executableEntry = entries.SingleOrDefault(entry => entry.IsoPath == ExecutablePath)
                ?? throw new InvalidDataException($"Required disc member missing: {ExecutablePath}");
            PsxExecutable executable = PsxExecutable.Parse(disc.ReadFile(executableEntry));
            var files = new EngineFiles(disc, entries, executable);
            FighterModelTable table = FighterModelTable.Parse(files.Read(FighterTableFileId).Bytes);

            var fighters = new List<BattleModelExtraction>(table.Entries.Count);
            foreach (FighterModelEntry entry in table.Entries)
                fighters.Add(ExtractFighter(table, entry, files, error));

            new DataPackWriter(outputDirectory).Write(new DataPackMetadata(
                typeof(BattleModelExtractionStep).Assembly.GetName().Version?.ToString(3) ?? "0.1.0",
                edition.BinSha1, edition.Label, DateTimeOffset.UtcNow),
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

    private static BattleModelExtraction ExtractFighter(FighterModelTable table, FighterModelEntry entry, EngineFiles files, TextWriter error)
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

    /// <summary>Reads disc members by engine file id through the executable's file-sector table.</summary>
    private sealed class EngineFiles
    {
        private readonly DiscSource _disc;
        private readonly DiscFileTable _table;
        private readonly IReadOnlyDictionary<string, DiscFileEntry> _byPath;

        internal EngineFiles(DiscSource disc, IReadOnlyList<DiscFileEntry> entries, PsxExecutable executable)
        {
            _disc = disc;
            DialogueRomLayout layout = DialogueRomLayout.Sles03936;
            // Zero-length members can share an LBA with a real file; only non-empty members are addressable.
            IReadOnlyDictionary<int, string> pathByLba = entries
                .Where(entry => entry.Size > 0)
                .GroupBy(entry => entry.Lba)
                .ToDictionary(group => group.Key, group => group.First().IsoPath);
            _table = DiscFileTable.Read(executable, layout.FileSectorsAddress, layout.FileSectorCount, pathByLba);
            _byPath = entries.Where(entry => entry.Size > 0).GroupBy(entry => entry.IsoPath).ToDictionary(group => group.Key, group => group.First());
        }

        /// <exception cref="FormatException">The id is outside the table or no disc member starts at its sector.</exception>
        internal (string Path, byte[] Bytes) Read(int fileId)
        {
            string path = _table.PathOf(fileId)
                ?? throw new FormatException($"Engine file 0x{fileId:X} does not start a disc member.");
            return (path, _disc.ReadFile(_byPath[path]));
        }
    }
}
