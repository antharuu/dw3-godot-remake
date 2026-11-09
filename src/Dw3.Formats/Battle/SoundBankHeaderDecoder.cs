using System.Buffers.Binary;

namespace Dw3.Formats.Battle;

/// <summary>Decodes the VAB header at the first entry of a battle MP sound-bank archive.</summary>
public static class SoundBankHeaderDecoder
{
    private const int VabHeaderBytes = 32;
    private const uint VabMagic = 0x56414270;

    /// <summary>Decodes one VAB header from the first present entry of an MP archive.</summary>
    /// <param name="archive">Raw MP archive bytes with a u32 offset table.</param>
    /// <returns>The decoded VAB bank metadata.</returns>
    /// <exception cref="FormatException">The archive lacks a valid first VAB entry.</exception>
    public static SoundBankHeader Decode(ReadOnlySpan<byte> archive)
    {
        IReadOnlyList<ArchiveRange> entries = ModelArchive.ReadBounds(archive);
        if (entries.Count == 0 || entries[0].End - entries[0].Start < VabHeaderBytes)
        {
            throw new FormatException("Sound bank archive has no complete VAB header entry.");
        }

        return DecodeVab(archive.Slice(entries[0].Start, entries[0].End - entries[0].Start));
    }

    /// <summary>Decodes and validates the fixed 32-byte VAB header.</summary>
    /// <param name="data">VAB entry bytes beginning with its signature.</param>
    /// <returns>The decoded VAB metadata.</returns>
    /// <exception cref="FormatException">The VAB signature or declared body metadata is invalid.</exception>
    public static SoundBankHeader DecodeVab(ReadOnlySpan<byte> data)
    {
        if (data.Length < VabHeaderBytes)
        {
            throw new FormatException("VAB header is truncated.");
        }

        if (BinaryPrimitives.ReadUInt32LittleEndian(data) != VabMagic)
        {
            throw new FormatException("VAB header signature is invalid.");
        }

        uint fileSize = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(12, 4));
        ushort programs = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(18, 2));
        ushort tones = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(20, 2));
        ushort samples = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(22, 2));
        if (fileSize < VabHeaderBytes || programs > 128)
        {
            throw new FormatException("VAB header declares an invalid size or program count.");
        }

        return new SoundBankHeader(
            BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(4, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(8, 4)),
            fileSize,
            programs,
            tones,
            samples,
            data[24],
            data[25]);
    }
}
