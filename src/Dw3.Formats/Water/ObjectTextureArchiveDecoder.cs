using System.Buffers.Binary;
using Dw3.Formats.Binary;

namespace Dw3.Formats.Water;

/// <summary>
/// The object VRAM rebuilt from a stage texture archive: one 64-word by 256-row page per
/// archive entry, laid side by side from VRAM (0x140, 0x100), plus the alternate palette
/// rows carried by entry zero for 8-bit parts.
/// </summary>
/// <param name="Pages">Row-major 16-bit VRAM words, <see cref="ObjectTextureArchiveDecoder.PageWords"/> by <see cref="ObjectTextureArchiveDecoder.PageRows"/> each.</param>
/// <param name="AlternatePalettes">Rows of 256 colour words, empty when entry zero has no palette block.</param>
public sealed record ObjectVram(IReadOnlyList<ushort[]> Pages, IReadOnlyList<ushort[]> AlternatePalettes);

/// <summary>
/// Decodes the stage texture archive <c>DAT/FIELD/STAGE/SnnnMASK.BIN</c>.
///
/// Archive layout: a table of u32 entry offsets terminated by a zero word, offsets
/// strictly increasing, each entry running to the next offset or the archive end.
/// Each entry is an optionally RLEN-packed TIM (magic 0x10). Its flag bit 3 announces
/// a palette block; only entry zero's palette (256 colours per row) is kept. The image
/// block must cover exactly one 64-word by 256-row VRAM page.
/// </summary>
public static class ObjectTextureArchiveDecoder
{
    /// <summary>Width of one VRAM page in 16-bit words.</summary>
    public const int PageWords = 64;

    /// <summary>Height of one VRAM page in rows.</summary>
    public const int PageRows = 256;

    private const int PaletteWidth = 256;
    private const uint TimMagic = 0x10;
    private const uint TimHasPalette = 8;
    private const int BlockHeaderSize = 12;

    /// <summary>Rebuilds the object VRAM from the raw texture archive.</summary>
    /// <exception cref="FormatException">The archive or one of its TIM entries is malformed.</exception>
    public static ObjectVram Decode(ReadOnlySpan<byte> archive)
    {
        var pages = new List<ushort[]>();
        var palettes = new List<ushort[]>();
        IReadOnlyList<(int Start, int End)> entries = ReadEntries(archive);
        for (int index = 0; index < entries.Count; index++)
        {
            ReadOnlySpan<byte> raw = archive[entries[index].Start..entries[index].End];
            byte[] data = Unpack(raw);
            if (data.Length < 20 || BinaryPrimitives.ReadUInt32LittleEndian(data) != TimMagic)
                throw new FormatException($"object archive entry={index} is not a packed TIM");
            uint flag = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4));
            int cursor = 8;
            if ((flag & TimHasPalette) != 0)
            {
                (uint size, int width, int height) = ReadBlockHeader(data, cursor);
                if (size < BlockHeaderSize || cursor + size > data.Length || width != PaletteWidth)
                    throw new FormatException("object archive CLUT dimensions invalid");
                if (index == 0)
                    palettes.AddRange(ReadRows(data.AsSpan(cursor + BlockHeaderSize, (int)size - BlockHeaderSize), width, height));
                cursor += (int)size;
            }

            (uint imageSize, int words, int rows) = ReadBlockHeader(data, cursor);
            int available = Math.Max(0, Math.Min(data.Length, cursor + (int)Math.Min(imageSize, int.MaxValue)) - (cursor + BlockHeaderSize));
            if (words != PageWords || rows != PageRows || available != PageWords * PageRows * 2)
                throw new FormatException("object archive image rectangle invalid");
            pages.Add(ReadWords(data.AsSpan(cursor + BlockHeaderSize, available)));
        }
        if (pages.Count == 0)
            throw new FormatException("object archive lacks any image entry");
        return new ObjectVram(pages, palettes);
    }

    private static IReadOnlyList<(int Start, int End)> ReadEntries(ReadOnlySpan<byte> archive)
    {
        var offsets = new List<int>();
        int cursor = 0;
        while (true)
        {
            if (cursor + 4 > archive.Length)
                throw new FormatException("archive offset table unterminated");
            uint offset = BinaryPrimitives.ReadUInt32LittleEndian(archive[cursor..]);
            cursor += 4;
            if (offset == 0)
                break;
            if (offset < cursor || offset >= archive.Length)
                throw new FormatException("archive offset outside payload");
            offsets.Add((int)offset);
        }
        if (offsets.Count == 0)
            throw new FormatException("archive offsets invalid");
        for (int index = 1; index < offsets.Count; index++)
            if (offsets[index] <= offsets[index - 1])
                throw new FormatException("archive offsets invalid");
        int archiveLength = archive.Length;
        return offsets.Select((start, index) => (start, index + 1 < offsets.Count ? offsets[index + 1] : archiveLength)).ToArray();
    }

    private static byte[] Unpack(ReadOnlySpan<byte> raw)
    {
        try
        {
            return RlenDecoder.DecompressIfNeeded(raw).Data;
        }
        catch (FormatException exception)
        {
            throw new FormatException($"object archive entry RLEN invalid: {exception.Message}", exception);
        }
    }

    private static (uint Size, int Width, int Height) ReadBlockHeader(byte[] data, int cursor)
    {
        if (cursor + BlockHeaderSize > data.Length)
            throw new FormatException("object archive image rectangle invalid");
        return (BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(cursor)),
            BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(cursor + 8)),
            BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(cursor + 10)));
    }

    private static IEnumerable<ushort[]> ReadRows(ReadOnlySpan<byte> raw, int width, int height)
    {
        if (raw.Length < width * height * 2)
            throw new FormatException("object archive CLUT dimensions invalid");
        var rows = new ushort[height][];
        for (int row = 0; row < height; row++)
            rows[row] = ReadWords(raw.Slice(row * width * 2, width * 2));
        return rows;
    }

    private static ushort[] ReadWords(ReadOnlySpan<byte> raw)
    {
        var words = new ushort[raw.Length / 2];
        for (int index = 0; index < words.Length; index++)
            words[index] = BinaryPrimitives.ReadUInt16LittleEndian(raw[(index * 2)..]);
        return words;
    }
}
