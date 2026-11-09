using System.Buffers.Binary;

namespace Dw3.Formats.Battle;

/// <summary>Splits an MV sound-bank body into VAB-sized SPU ADPCM samples and decodes every sample.</summary>
public static class SoundBankBodyDecoder
{
    private const int BodyPrefixBytes = 4;

    /// <summary>Decodes every sample body described by the VAB's fixed trailing length table.</summary>
    /// <param name="headerArchive">Raw MP archive carrying the first VAB entry.</param>
    /// <param name="bodyArchive">Raw MV body with its four-byte prefix.</param>
    /// <returns>One decoded PCM sample for each VAB sample body.</returns>
    /// <exception cref="FormatException">The MP/MV pair is malformed or its lengths disagree.</exception>
    public static IReadOnlyList<DecodedSpuAdpcmSample> Decode(ReadOnlySpan<byte> headerArchive, ReadOnlySpan<byte> bodyArchive)
    {
        SoundBankHeader header = SoundBankHeaderDecoder.Decode(headerArchive);
        int[] lengths = DecodeLengths(headerArchive, header.SampleCount);
        ValidateBodyPrefix(bodyArchive);
        int expectedBytes = lengths.Sum() + BodyPrefixBytes;
        if (bodyArchive.Length != expectedBytes)
        {
            throw new FormatException("MV body length does not match the VAB sample length table.");
        }

        return DecodeSamples(bodyArchive.Slice(BodyPrefixBytes), lengths);
    }

    /// <summary>Reads the fixed 256-entry VAB sample-length table at the end of its first archive entry.</summary>
    /// <param name="headerArchive">Raw MP archive bytes.</param>
    /// <param name="sampleCount">Number of samples declared by the VAB header.</param>
    /// <returns>One byte length per declared sample.</returns>
    private static int[] DecodeLengths(ReadOnlySpan<byte> headerArchive, ushort sampleCount)
    {
        IReadOnlyList<ArchiveRange> entries = ModelArchive.ReadBounds(headerArchive);
        ArchiveRange entry = entries[0];
        const int tableBytes = 512;
        if (entry.End - entry.Start < tableBytes || sampleCount >= 256)
        {
            throw new FormatException("VAB sample length table is truncated or unsupported.");
        }

        ReadOnlySpan<byte> table = headerArchive.Slice(entry.End - tableBytes, tableBytes);
        if (BinaryPrimitives.ReadUInt16LittleEndian(table) != 0)
        {
            throw new FormatException("VAB sample length table must begin with zero.");
        }

        var lengths = new int[sampleCount];
        for (int index = 0; index < lengths.Length; index++)
        {
            ushort units = BinaryPrimitives.ReadUInt16LittleEndian(table.Slice((index + 1) * 2, 2));
            if (units == 0)
            {
                throw new FormatException($"VAB sample {index} has zero encoded length.");
            }

            lengths[index] = units * 8;
        }

        return lengths;
    }

    /// <summary>Checks the MV's documented four-byte prefix before its block-aligned sound bodies.</summary>
    /// <param name="bodyArchive">Raw MV body archive.</param>
    private static void ValidateBodyPrefix(ReadOnlySpan<byte> bodyArchive)
    {
        if (bodyArchive.Length < BodyPrefixBytes || BinaryPrimitives.ReadUInt32LittleEndian(bodyArchive) != BodyPrefixBytes)
        {
            throw new FormatException("MV body prefix is invalid.");
        }
    }

    /// <summary>Decodes the contiguous sample records after the MV prefix.</summary>
    /// <param name="body">Bytes after the MV prefix.</param>
    /// <param name="lengths">Encoded byte length of each sample.</param>
    /// <returns>Decoded sample sequence.</returns>
    private static List<DecodedSpuAdpcmSample> DecodeSamples(ReadOnlySpan<byte> body, int[] lengths)
    {
        var decoded = new List<DecodedSpuAdpcmSample>(lengths.Length);
        int offset = 0;
        foreach (int length in lengths)
        {
            decoded.Add(SpuAdpcmDecoder.Decode(body.Slice(offset, length)));
            offset += length;
        }

        return decoded;
    }
}
