using System.Buffers.Binary;
using Dw3.Formats.Binary;

namespace Dw3.Formats.Field;

/// <summary>Decodes RLEN-wrapped direct-colour TIM pages embedded by FIELDCOM entries.</summary>
public static class FieldcomVramDecoder
{
    private const int PageWords = 64;
    private const int PageRows = 256;
    private const uint TimMagic = 0x10;
    private const uint DirectColourFlag = 2;

    /// <summary>
    /// Decodes a list of FIELDCOM VRAM archive entries into a <see cref="FieldcomVram"/> instance.
    /// Each entry must be an RLEN-compressed direct-colour (15-bit) TIM page.
    /// </summary>
    /// <param name="entries">VRAM archive entries, one per texture page, in page-index order.</param>
    /// <returns>The decoded VRAM with all pages available for sprite compositing.</returns>
    /// <exception cref="FormatException">An entry is not the expected RLEN direct-colour TIM page.</exception>
    public static FieldcomVram Decode(IReadOnlyList<ReadOnlyMemory<byte>> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count == 0)
        {
            throw new FormatException("FIELDCOM has no VRAM pages");
        }

        var pages = new ushort[entries.Count][];
        for (int index = 0; index < entries.Count; index++)
        {
            pages[index] = DecodePage(entries[index].Span);
        }

        return new FieldcomVram(pages);
    }

    /// <summary>
    /// Decompresses and decodes one FIELDCOM VRAM page entry from RLEN + direct-colour TIM format.
    /// The entry has a leading 4-byte offset (must equal 8), followed by an RLEN block starting at +8.
    /// </summary>
    /// <param name="entry">Raw bytes of one VRAM archive entry.</param>
    /// <returns>Raw pixel halfwords in row-major order, PageWords × PageRows values.</returns>
    /// <exception cref="FormatException">The entry offset is wrong, the RLEN is invalid, or the TIM header is malformed.</exception>
    private static ushort[] DecodePage(ReadOnlySpan<byte> entry)
    {
        if (entry.Length < 9 || BinaryPrimitives.ReadUInt32LittleEndian(entry) != 8)
        {
            throw new FormatException("FIELDCOM VRAM entry offset is invalid");
        }

        byte[] data;
        try
        {
            data = RlenDecoder.Decompress(entry[8..]);
        }
        catch (FormatException exception)
        {
            throw new FormatException($"FIELDCOM VRAM RLEN is invalid: {exception.Message}", exception);
        }

        ValidateTimHeader(data);

        var page = new ushort[PageWords * PageRows];
        for (int index = 0; index < page.Length; index++)
        {
            page[index] = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(20 + (index * sizeof(ushort))));
        }

        return page;
    }

    /// <summary>
    /// Validates that <paramref name="data"/> is a direct-colour 15-bit TIM image block of the expected dimensions.
    /// Throws <see cref="FormatException"/> when the magic, flags or dimensions are wrong.
    /// </summary>
    /// <param name="data">Decompressed TIM bytes starting with magic 0x10.</param>
    /// <exception cref="FormatException">The TIM is not a 15-bit direct-colour image or its dimensions do not match the FIELDCOM page size.</exception>
    private static void ValidateTimHeader(byte[] data)
    {
        if (data.Length < 20 || BinaryPrimitives.ReadUInt32LittleEndian(data) != TimMagic ||
            BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4)) != DirectColourFlag)
        {
            throw new FormatException("FIELDCOM VRAM page TIM header is invalid");
        }

        uint blockSize = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(8));
        ushort words = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(16));
        ushort rows = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(18));
        int pixels = PageWords * PageRows * sizeof(ushort);
        if (blockSize != data.Length - 8 || words != PageWords || rows != PageRows || data.Length != 20 + pixels)
        {
            throw new FormatException("FIELDCOM VRAM page rectangle is invalid");
        }
    }
}
