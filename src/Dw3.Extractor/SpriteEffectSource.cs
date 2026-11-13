using Dw3.Formats.Battle;
using Dw3.Formats.Binary;

namespace Dw3.Extractor;

/// <summary>Resolves packed sprite selectors and resident or effect-local texture archives.</summary>
internal sealed class SpriteEffectSource
{
    private readonly EngineFileReader _files;
    private readonly Dictionary<int, byte[]> _archives = [];

    /// <summary>Initializes a new instance of the <see cref="SpriteEffectSource"/> class.</summary>
    /// <param name="files">User-disc file resolver.</param>
    internal SpriteEffectSource(EngineFileReader files) => _files = files;

    /// <summary>Reads one present archive entry with explicit index and bounds checks.</summary>
    /// <param name="archive">Offset-table archive.</param>
    /// <param name="index">Member index.</param>
    /// <returns>Bounded member bytes.</returns>
    internal static byte[] Entry(byte[] archive, int index)
    {
        IReadOnlyList<ArchiveRange> bounds = ModelArchive.ReadBounds(archive);
        if (index < 0 || index >= bounds.Count)
        {
            throw new FormatException("Sprite archive index is out of bounds.");
        }

        ArchiveRange range = bounds[index];
        if (range.IsEmpty)
        {
            throw new SpriteEffectSourceAbsentException($"Sprite archive member {index} has a zero offset in archive SHA256 {Dw3.Disc.DiscIdentity.Sha256Bytes(archive)}.");
        }

        return archive[range.Start..range.End];
    }

    /// <summary>Reads a bounded selector without treating malformed entries as absent source.</summary>
    /// <param name="selector">High word file id and low word member index.</param>
    /// <returns>Present member bytes.</returns>
    internal byte[] Read(uint selector)
    {
        int file = (int)(selector >> 16);
        if (!_archives.TryGetValue(file, out byte[]? archive))
        {
            archive = _files.Read(file).Bytes;
            _archives.Add(file, archive);
        }

        return Entry(archive, (int)(selector & 0xFFFF));
    }

    /// <summary>Loads all texture pages, preserving their archive indices.</summary>
    /// <param name="sheet">Sheet reference from the overlay.</param>
    /// <returns>Decoded four-bit texture pages.</returns>
    internal IReadOnlyList<HudTexturePage> Pages(SpriteEffectSheetReference sheet)
    {
        byte[] archive = TextureArchive(sheet);
        IReadOnlyList<ArchiveRange> ranges = ModelArchive.ReadBounds(archive);
        int count = ranges.TakeWhile(range => !range.IsEmpty).Count();
        if (count == 0 || ranges.Skip(count).Any(range => !range.IsEmpty))
        {
            throw new FormatException("Sparse or empty sprite texture pages are unsupported, not compacted.");
        }

        return [.. ranges.Take(count).Select(range => HudTexturePageDecoder.DecodeFourBitTim(RlenDecoder.Decompress(archive.AsSpan(range.Start, range.End - range.Start))))];
    }

    /// <summary>Returns the exact resident or effect-local texture archive for provenance.</summary>
    /// <param name="sheet">Qualified sheet selector.</param>
    /// <returns>Original bounded texture archive bytes.</returns>
    internal byte[] TextureArchive(SpriteEffectSheetReference sheet) => sheet.TextureSelector == 0
        ? _files.Read(CommonTextureFile(sheet.Index)).Bytes : Read(sheet.TextureSelector);

    /// <summary>Maps the four resident common sheets to their explicit texture files.</summary>
    /// <param name="index">Resident sheet index.</param>
    /// <returns>Texture file identity.</returns>
    private static int CommonTextureFile(int index) => index switch
    {
        0 => 0x79E,
        1 => 0x7AE,
        2 => 0x7E1,
        3 => 0x7E3,
        _ => throw new FormatException("Sprite sheet has no texture source."),
    };
}
