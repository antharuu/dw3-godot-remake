using System.Buffers.Binary;
using Dw3.Formats.Binary;

namespace Dw3.Formats.Water;

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

    /// <summary>Rebuilds object VRAM pages and entry-zero alternate palettes from the raw archive.</summary>
    /// <param name="archive">Raw <c>SnnnMASK.BIN</c> archive bytes.</param>
    /// <returns>The reconstructed object pages and alternate palettes.</returns>
    /// <exception cref="FormatException">The archive or one of its TIM entries is malformed.</exception>
    public static ObjectVram Decode(ReadOnlySpan<byte> archive)
    {
        var pages = new List<ushort[]>();
        var palettes = new List<ushort[]>();
        (int Start, int End)[] entries = ReadEntries(archive);
        for (int index = 0; index < entries.Length; index++)
        {
            ReadOnlySpan<byte> raw = archive[entries[index].Start..entries[index].End];
            pages.Add(DecodeEntry(raw, index, palettes));
        }

        if (pages.Count == 0)
        {
            throw new FormatException("object archive lacks any image entry");
        }

        return new ObjectVram(pages, palettes);
    }

    /// <summary>Decodes one archive member and retains entry-zero palettes.</summary>
    /// <param name="raw">The bounded raw archive member.</param>
    /// <param name="index">Member index in the archive offset table.</param>
    /// <param name="palettes">Destination for alternate entry-zero palette rows.</param>
    /// <returns>The reconstructed 64-word by 256-row object page.</returns>
    private static ushort[] DecodeEntry(ReadOnlySpan<byte> raw, int index, List<ushort[]> palettes)
    {
        byte[] data = Unpack(raw);
        ValidateTim(data, index);
        int cursor = ReadPalette(data, index, palettes);
        return ReadImage(data, cursor);
    }

    private static void ValidateTim(byte[] data, int index)
    {
        if (data.Length < 20 || BinaryPrimitives.ReadUInt32LittleEndian(data) != TimMagic)
        {
            throw new FormatException($"object archive entry={index} is not a packed TIM");
        }
    }

    private static int ReadPalette(byte[] data, int index, List<ushort[]> palettes)
    {
        if ((BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4)) & TimHasPalette) == 0)
        {
            return 8;
        }

        const int cursor = 8;
        (uint size, int width, int height) = ReadBlockHeader(data, cursor);
        if (size < BlockHeaderSize || cursor + size > data.Length || width != PaletteWidth)
        {
            throw new FormatException("object archive CLUT dimensions invalid");
        }

        if (index == 0)
        {
            palettes.AddRange(ReadRows(data.AsSpan(cursor + BlockHeaderSize, (int)size - BlockHeaderSize), width, height));
        }

        return cursor + (int)size;
    }

    private static ushort[] ReadImage(byte[] data, int cursor)
    {
        (uint imageSize, int words, int rows) = ReadBlockHeader(data, cursor);
        int available = Math.Max(0, Math.Min(data.Length, cursor + (int)Math.Min(imageSize, int.MaxValue)) - (cursor + BlockHeaderSize));
        if (words != PageWords || rows != PageRows || available != PageWords * PageRows * 2)
        {
            throw new FormatException("object archive image rectangle invalid");
        }

        return ReadWords(data.AsSpan(cursor + BlockHeaderSize, available));
    }

    /// <summary>Reads ordered archive entry bounds from the offset table.</summary>
    /// <param name="archive">Raw archive bytes containing the offset table.</param>
    /// <returns>Inclusive-start, exclusive-end bounds for every archive entry.</returns>
    private static (int Start, int End)[] ReadEntries(ReadOnlySpan<byte> archive)
    {
        var offsets = new List<int>();
        int cursor = 0;
        while (true)
        {
            if (cursor + 4 > archive.Length)
            {
                throw new FormatException("archive offset table unterminated");
            }

            uint offset = BinaryPrimitives.ReadUInt32LittleEndian(archive[cursor..]);
            cursor += 4;
            if (offset == 0)
            {
                break;
            }

            if (offset < cursor || offset >= archive.Length)
            {
                throw new FormatException("archive offset outside payload");
            }

            offsets.Add((int)offset);
        }

        if (offsets.Count == 0)
        {
            throw new FormatException("archive offsets invalid");
        }

        ValidateIncreasingOffsets(offsets);
        int archiveLength = archive.Length;
        return [.. offsets.Select((start, index) => (start, index + 1 < offsets.Count ? offsets[index + 1] : archiveLength))];
    }

    private static void ValidateIncreasingOffsets(List<int> offsets)
    {
        for (int index = 1; index < offsets.Count; index++)
        {
            if (offsets[index] <= offsets[index - 1])
            {
                throw new FormatException("archive offsets invalid");
            }
        }
    }

    /// <summary>Unpacks one RLEN-wrapped or raw TIM entry.</summary>
    /// <param name="raw">Archive entry bytes.</param>
    /// <returns>Unpacked TIM bytes.</returns>
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

    /// <summary>Reads a TIM block header at its current entry cursor.</summary>
    /// <param name="data">Unpacked TIM bytes.</param>
    /// <param name="cursor">Byte offset of the block header.</param>
    /// <returns>The declared block size and rectangle dimensions.</returns>
    private static (uint Size, int Width, int Height) ReadBlockHeader(byte[] data, int cursor)
    {
        if (cursor + BlockHeaderSize > data.Length)
        {
            throw new FormatException("object archive image rectangle invalid");
        }

        return (BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(cursor)),
            BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(cursor + 8)),
            BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(cursor + 10)));
    }

    /// <summary>Reads palette rows of 16-bit colour words.</summary>
    /// <param name="raw">Packed palette payload.</param>
    /// <param name="width">Words in one palette row.</param>
    /// <param name="height">Number of palette rows.</param>
    /// <returns>The decoded palette rows.</returns>
    private static ushort[][] ReadRows(ReadOnlySpan<byte> raw, int width, int height)
    {
        if (raw.Length < width * height * 2)
        {
            throw new FormatException("object archive CLUT dimensions invalid");
        }

        var rows = new ushort[height][];
        for (int row = 0; row < height; row++)
        {
            rows[row] = ReadWords(raw.Slice(row * width * 2, width * 2));
        }

        return rows;
    }

    /// <summary>Reads contiguous little-endian colour words.</summary>
    /// <param name="raw">Even-length word payload.</param>
    /// <returns>Decoded 16-bit words.</returns>
    private static ushort[] ReadWords(ReadOnlySpan<byte> raw)
    {
        var words = new ushort[raw.Length / 2];
        for (int index = 0; index < words.Length; index++)
        {
            words[index] = BinaryPrimitives.ReadUInt16LittleEndian(raw[(index * 2)..]);
        }

        return words;
    }
}
