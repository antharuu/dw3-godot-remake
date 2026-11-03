using Dw3.Disc;

namespace Dw3.Extractor.Tests;

/// <summary>
/// In-memory disc made of fabricated files. Each file gets the next free LBA (files never share
/// a sector), so the extractor can resolve members by path or by start sector exactly as it does
/// on a BIN image.
/// </summary>
internal sealed class FabricatedDisc : IDiscSource
{
    internal const int SectorBytes = 2048;

    internal static readonly SupportedEdition Edition = new("Fabricated test disc", new string('a', 32), new string('b', 40));

    private readonly List<DiscFileEntry> _entries = [];
    private readonly Dictionary<string, byte[]> _contents = new(StringComparer.Ordinal);
    private int _nextLba = 32;

    internal bool Unsupported { get; init; }

    internal int OpenHandles { get; private set; }

    internal int Opened { get; private set; }

    public string ReadVolumeId() => "DMW3";

    public IReadOnlyList<DiscFileEntry> ListFiles() => [.. _entries];

    public byte[] ReadFile(DiscFileEntry entry) =>
        _contents.TryGetValue(entry.IsoPath, out byte[]? content)
            ? [.. content]
            : throw new DiscSourceException(DiscSourceException.FormatInvalid, $"No fabricated file {entry.IsoPath}.");

    public SupportedEdition IdentifyEdition() =>
        Unsupported
            ? throw new DiscSourceException(DiscSourceException.EditionInvalid, "Fabricated disc is not a supported edition.")
            : Edition;

    public void Dispose() => OpenHandles--;

    internal FabricatedDisc Add(string isoPath, byte[] content)
    {
        int lba = _nextLba;
        _nextLba += Math.Max(1, (content.Length + SectorBytes - 1) / SectorBytes);
        return AddAt(isoPath, lba, content);
    }

    internal FabricatedDisc AddAt(string isoPath, int lba, byte[] content)
    {
        _entries.Add(new DiscFileEntry(isoPath, lba, content.Length));
        _contents[isoPath] = content;
        _nextLba = Math.Max(_nextLba, lba + Math.Max(1, (content.Length + SectorBytes - 1) / SectorBytes));
        return this;
    }

    internal int LbaOf(string isoPath) => _entries.Single(entry => entry.IsoPath == isoPath).Lba;

    /// <summary>Returns this disc as the result of an "open" call, counting the handle until disposed.</summary>
    internal IDiscSource Open()
    {
        Opened++;
        OpenHandles++;
        return this;
    }
}
