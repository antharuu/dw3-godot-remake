using System.Buffers.Binary;
using Dw3.Formats.Binary;

namespace Dw3.Formats.Battle;

/// <summary>
/// The private VRAM a model texture archive rebuilds before its pages are sampled: every
/// archive entry is a 16-bit TIM upload placed at column entry × 0x40 halfwords, row 0.
/// </summary>
/// <remarks>
/// Archive: up to 16 u32 entry offsets terminated by 0, strictly increasing and inside the file;
/// each entry runs to the next one or to the end. An entry may be RLEN-compressed. Each upload is
/// a TIM with magic 0x10, flags 2 (15-bit, no CLUT block), image block size at +8, width in
/// halfwords at +16, height at +18 and pixels from +20.
/// </remarks>
internal sealed class BattleLocalVram
{
    private const int MaximumTableSlots = 17;
    private const int UploadColumnStride = 0x40;
    private const int PageRows = 256;

    private BattleLocalVram(ushort[] words, BattleTextureVram layout)
    {
        Words = words;
        Layout = layout;
    }

    /// <summary>Gets the VRAM halfwords, row-major with <see cref="BattleTextureVram.WidthWords"/> per row.</summary>
    public ushort[] Words { get; }

    /// <summary>Gets the VRAM size and where each upload landed.</summary>
    public BattleTextureVram Layout { get; }

    /// <summary>Gets the VRAM width in halfwords.</summary>
    public int Width => Layout.WidthWords;

    /// <summary>Gets the VRAM height in rows.</summary>
    public int Height => Layout.Height;

    /// <summary>Uploads every entry of <paramref name="archive"/> into a fresh local VRAM.</summary>
    /// <param name="archive">Texture archive entry of a model file.</param>
    /// <returns>The rebuilt VRAM.</returns>
    /// <exception cref="FormatException">The archive table or an upload is malformed, or the uploads exceed 256 rows.</exception>
    public static BattleLocalVram Build(byte[] archive)
    {
        IReadOnlyList<byte[]> entries = ReadEntries(archive);
        var uploads = new List<(int X, int Width, int Height, ushort[] Words)>();
        int maxX = 0;
        int maxY = 0;
        for (int i = 0; i < entries.Count; i++)
        {
            byte[] raw = RlenDecoder.DecompressIfNeeded(entries[i]).Data;
            (int width, int height, ushort[] words) = ReadTimUpload(raw);
            int x = i * UploadColumnStride;
            uploads.Add((x, width, height, words));
            maxX = Math.Max(maxX, x + width);
            maxY = Math.Max(maxY, height);
        }

        if (maxY > PageRows)
        {
            throw new FormatException($"texture archive height {maxY} exceeds a PS1 texture page");
        }

        var vram = new ushort[maxX * maxY];
        foreach (var upload in uploads)
        {
            for (int y = 0; y < upload.Height; y++)
            {
                upload.Words.AsSpan(y * upload.Width, upload.Width).CopyTo(vram.AsSpan((y * maxX) + upload.X, upload.Width));
            }
        }

        var layout = new BattleTextureVram(maxX, maxY, [.. uploads.Select(u => new BattleTextureUpload(u.X, u.Width, u.Height))]);
        return new BattleLocalVram(vram, layout);
    }

    /// <summary>Splits the archive into its entries.</summary>
    /// <param name="archive">Texture archive bytes.</param>
    /// <returns>Entry bytes in table order.</returns>
    /// <exception cref="FormatException">The table is short, unterminated, empty or holds an out-of-order offset.</exception>
    private static IReadOnlyList<byte[]> ReadEntries(byte[] archive)
    {
        if (archive.Length < 4)
        {
            throw new FormatException("texture archive too short");
        }

        List<int> offsets = ReadEntryOffsets(archive);
        if (offsets.Count == 0)
        {
            throw new FormatException("texture archive has no entries");
        }

        return [.. offsets.Select((start, index) => archive[start..(index + 1 < offsets.Count ? offsets[index + 1] : archive.Length)])];
    }

    /// <summary>Reads the zero-terminated offset table; at most 16 entries precede the terminator.</summary>
    /// <param name="archive">Texture archive bytes, at least 4 long.</param>
    /// <returns>The entry offsets, possibly empty.</returns>
    /// <exception cref="FormatException">The table runs off the data, has no terminator in 17 slots, or an offset is out of range.</exception>
    private static List<int> ReadEntryOffsets(byte[] archive)
    {
        var offsets = new List<int>();
        for (int index = 0; index < MaximumTableSlots; index++)
        {
            if ((index * 4) + 4 > archive.Length)
            {
                throw new FormatException("texture archive table lacks terminator");
            }

            uint raw = BinaryPrimitives.ReadUInt32LittleEndian(archive.AsSpan(index * 4, 4));
            if (raw == 0)
            {
                return offsets;
            }

            if (raw > int.MaxValue || raw < (index + 1) * 4 || raw >= archive.Length || (offsets.Count > 0 && raw <= offsets[^1]))
            {
                throw new FormatException($"texture archive entry {index} offset out of range");
            }

            offsets.Add((int)raw);
        }

        throw new FormatException("texture archive has too many entries");
    }

    /// <summary>Reads one 16-bit TIM upload.</summary>
    /// <param name="data">Decompressed TIM.</param>
    /// <returns>Width in halfwords, height in rows and the pixel words.</returns>
    /// <exception cref="FormatException">The TIM is not a CLUT-less 16-bit image or its size disagrees with its dimensions.</exception>
    private static (int Width, int Height, ushort[] Words) ReadTimUpload(byte[] data)
    {
        if (data.Length < 20 || BinaryPrimitives.ReadUInt32LittleEndian(data) != 0x10 || BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4)) != 2)
        {
            throw new FormatException("expected 16-bit TIM VRAM upload without a separate CLUT");
        }

        uint block = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(8));
        if (block < 12 || 8L + block > data.Length)
        {
            throw new FormatException("invalid TIM VRAM upload image block");
        }

        int width = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(16));
        int height = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(18));
        if (width <= 0 || height <= 0 || 20L + (width * (long)height * 2) != 8L + block)
        {
            throw new FormatException("TIM VRAM upload dimensions do not match payload");
        }

        var words = new ushort[width * height];
        for (int i = 0; i < words.Length; i++)
        {
            words[i] = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(20 + (i * 2)));
        }

        return (width, height, words);
    }
}
