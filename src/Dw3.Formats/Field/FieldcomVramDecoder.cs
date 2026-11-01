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
