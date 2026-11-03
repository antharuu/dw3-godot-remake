using Dw3.Disc;
using Dw3.Formats.Binary;
using Dw3.Formats.Dialogue;

namespace Dw3.Extractor;

/// <summary>
/// Reads disc members by engine file id. The executable stores a FILE_SECTORS table (one 32-bit
/// start LBA per file id, located by <see cref="DialogueRomLayout.Sles03936"/>); an id names a
/// disc member when its LBA is the first sector of a non-empty ISO9660 file. The battle steps
/// address their sources this way because the executable refers to them by id, not by path.
/// </summary>
internal sealed class EngineFileReader
{
    private readonly IDiscSource _disc;
    private readonly DiscFileTable _table;
    private readonly Dictionary<string, DiscFileEntry> _byPath;

    /// <summary>
    /// Initializes a new instance of the <see cref="EngineFileReader"/> class by reading the
    /// FILE_SECTORS table of <paramref name="executable"/> against the LBAs of <paramref name="entries"/>.
    /// </summary>
    /// <param name="disc">The open disc that later serves the bytes of each resolved member.</param>
    /// <param name="entries">The disc listing. Zero-length members are ignored because they can share
    /// an LBA with the real file that follows them.</param>
    /// <param name="executable">The parsed executable that carries the FILE_SECTORS table.</param>
    /// <exception cref="FormatException">The FILE_SECTORS table lies outside the executable image.</exception>
    internal EngineFileReader(IDiscSource disc, IReadOnlyList<DiscFileEntry> entries, PsxExecutable executable)
    {
        _disc = disc;
        DialogueRomLayout layout = DialogueRomLayout.Sles03936;
        IReadOnlyDictionary<int, string> pathByLba = entries
            .Where(entry => entry.Size > 0)
            .GroupBy(entry => entry.Lba)
            .ToDictionary(group => group.Key, group => group.First().IsoPath);
        _table = DiscFileTable.Read(executable, layout.FileSectorsAddress, layout.FileSectorCount, pathByLba);
        _byPath = entries
            .Where(entry => entry.Size > 0)
            .GroupBy(entry => entry.IsoPath)
            .ToDictionary(group => group.Key, group => group.First());
    }

    /// <summary>Reads the disc member that starts at the sector recorded for <paramref name="fileId"/>.</summary>
    /// <param name="fileId">The 0-based index into the FILE_SECTORS table.</param>
    /// <returns>The ISO path of the member and its user-data bytes.</returns>
    /// <exception cref="FormatException">The id is outside the table or no non-empty member starts at its sector.</exception>
    internal (string Path, byte[] Bytes) Read(int fileId)
    {
        string path = _table.PathOf(fileId)
            ?? throw new FormatException($"Engine file 0x{fileId:X} does not start a disc member.");
        return (path, _disc.ReadFile(_byPath[path]));
    }
}
