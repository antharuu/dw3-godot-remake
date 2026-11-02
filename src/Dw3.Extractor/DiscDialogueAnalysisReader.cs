using Dw3.Disc;
using Dw3.Formats.Binary;
using Dw3.Formats.Dialogue;

namespace Dw3.Extractor;

/// <summary>
/// Runs the stage dialogue analysis on the user's disc: reads SLES_039.36, FIELDSTG.PRO and every
/// WSTAG overlay of the field-stage registry, and hands them to <see cref="RomAnalysis"/>.
/// </summary>
internal static class DiscDialogueAnalysisReader
{
    /// <summary>Country folder whose language copy of the talk files the remake uses (French).</summary>
    public const string CountryFolder = "FRA";

    private const string ExecutablePath = "SLES_039.36";
    private const string FieldStgPath = "AAA/PRO/FIELDSTG.PRO";

    /// <summary>
    /// Reads the SLES executable, the FIELDSTG overlay and every WSTAG stage overlay from the disc,
    /// then analyses all field stages for FieldTalk dialogue data.
    /// </summary>
    /// <param name="disc">The open disc source to read files from.</param>
    /// <param name="entries">The full list of disc file entries (from disc.ListFiles()).</param>
    /// <returns>The complete analysis result including stage analyses, failures, and progress tables.</returns>
    /// <exception cref="InvalidDataException">A required disc file is absent.</exception>
    public static RomAnalysisResult Read(DiscSource disc, IReadOnlyList<DiscFileEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(disc);
        ArgumentNullException.ThrowIfNull(entries);
        DialogueRomLayout layout = DialogueRomLayout.Sles03936;
        PsxExecutable executable = PsxExecutable.Parse(disc.ReadFile(Require(entries, ExecutablePath)));
        IReadOnlyList<int> widths = layout.ReadActorWidths(disc.ReadFile(Require(entries, FieldStgPath)));
        // Zero-length members may share an LBA with the next file; the first directory entry wins.
        IReadOnlyDictionary<int, string> pathByLba = entries
            .GroupBy(entry => entry.Lba)
            .ToDictionary(group => group.Key, group => group.First().IsoPath);
        DiscFileTable files = DiscFileTable.Read(executable, layout.FileSectorsAddress, layout.FileSectorCount, pathByLba);

        IEnumerable<StageOverlay> stages = DiscStageModeReader.BuildRegistry(disc, entries)
            .Select(stage => new StageOverlay(
                stage.Mode,
                stage.StageId,
                disc.ReadFile(Require(entries, $"AAA/PRO/WSTAG{stage.StageId:D3}.PRO"))));
        return RomAnalysis.Analyze(executable, layout, files, CountryFolder, widths, stages);
    }

    /// <summary>
    /// Returns the first disc entry with a path matching <paramref name="path"/> exactly (ordinal),
    /// throwing <see cref="InvalidDataException"/> when no such entry exists.
    /// </summary>
    /// <param name="entries">The disc file entry list to search.</param>
    /// <param name="path">The ISO path to find.</param>
    /// <returns>The matching <see cref="DiscFileEntry"/>.</returns>
    /// <exception cref="InvalidDataException">The required disc file is absent.</exception>
    private static DiscFileEntry Require(IReadOnlyList<DiscFileEntry> entries, string path) =>
        entries.SingleOrDefault(entry => string.Equals(entry.IsoPath, path, StringComparison.Ordinal))
        ?? throw new InvalidDataException($"Required disc file is absent: {path}");
}
