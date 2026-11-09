namespace Dw3.Formats.Battle;

/// <summary>Decodes one mono PlayStation SPU ADPCM sample stream into signed PCM samples.</summary>
public static class SpuAdpcmDecoder
{
    private static readonly int[] FilterA = [0, 60, 115, 98, 122];
    private static readonly int[] FilterB = [0, 0, -52, -55, -60];

    /// <summary>Decodes blocks through their first terminal flag and validates terminal padding.</summary>
    /// <param name="encoded">Sixteen-byte SPU ADPCM blocks for one sample.</param>
    /// <returns>PCM samples and validated loop metadata.</returns>
    /// <exception cref="FormatException">The block stream has invalid headers or terminal flags.</exception>
    public static DecodedSpuAdpcmSample Decode(ReadOnlySpan<byte> encoded)
    {
        ValidateBlockAlignment(encoded);
        List<short> pcm = [];
        pcm.EnsureCapacity((encoded.Length / 16) * 28);
        int history1 = 0;
        int history2 = 0;
        int? loopStart = null;
        for (int offset = 0; offset < encoded.Length; offset += 16)
        {
            byte flags = encoded[offset + 1];
            ValidateFlags(flags);
            if ((flags & 4) != 0)
            {
                loopStart = pcm.Count;
            }

            DecodeBlock(encoded.Slice(offset, 16), pcm, ref history1, ref history2);
            if ((flags & 1) != 0)
            {
                ValidateTerminalPadding(encoded.Slice(offset + 16));
                bool loops = (flags & 2) != 0;
                return new([.. pcm], offset + 16, loops, loops ? loopStart ?? 0 : loopStart);
            }
        }

        throw new FormatException("SPU ADPCM sample has no terminal block.");
    }

    /// <summary>Checks that the source can contain whole SPU ADPCM blocks.</summary>
    /// <param name="encoded">Raw encoded bytes.</param>
    private static void ValidateBlockAlignment(ReadOnlySpan<byte> encoded)
    {
        if (encoded.Length == 0 || encoded.Length % 16 != 0)
        {
            throw new FormatException("SPU ADPCM sample must contain one or more 16-byte blocks.");
        }
    }

    /// <summary>Rejects flag bits outside the documented terminal and loop mask.</summary>
    /// <param name="flags">Block flags.</param>
    private static void ValidateFlags(byte flags)
    {
        if ((flags & ~7) != 0)
        {
            throw new FormatException("SPU ADPCM block has unknown flags.");
        }
    }

    /// <summary>Decodes the 28 nibbles carried by one SPU ADPCM block.</summary>
    /// <param name="block">One complete source block.</param>
    /// <param name="pcm">Destination PCM collection.</param>
    /// <param name="history1">Most recent decoded sample.</param>
    /// <param name="history2">Second-most recent decoded sample.</param>
    private static void DecodeBlock(ReadOnlySpan<byte> block, List<short> pcm, ref int history1, ref int history2)
    {
        int filter = block[0] >> 4;
        int shift = block[0] & 15;
        if (filter >= FilterA.Length || shift > 12)
        {
            throw new FormatException("SPU ADPCM block has an invalid predictor or shift.");
        }

        for (int index = 2; index < 16; index++)
        {
            DecodeNibble((sbyte)((sbyte)(block[index] << 4) >> 4), shift, filter, pcm, ref history1, ref history2);
            DecodeNibble((sbyte)((sbyte)block[index] >> 4), shift, filter, pcm, ref history1, ref history2);
        }
    }

    /// <summary>Expands one signed four-bit value using the current predictor history.</summary>
    /// <param name="nibble">Signed source nibble.</param>
    /// <param name="shift">ADPCM shift amount.</param>
    /// <param name="filter">Predictor index.</param>
    /// <param name="pcm">Destination PCM collection.</param>
    /// <param name="history1">Most recent decoded sample.</param>
    /// <param name="history2">Second-most recent decoded sample.</param>
    private static void DecodeNibble(sbyte nibble, int shift, int filter, List<short> pcm, ref int history1, ref int history2)
    {
        int quantized = (nibble << 12) >> shift;
        int predicted = ((history1 * FilterA[filter]) + (history2 * FilterB[filter]) + 32) >> 6;
        int decoded = quantized + predicted;
        decoded = Math.Clamp(decoded, short.MinValue, short.MaxValue);
        history2 = history1;
        history1 = decoded;
        pcm.Add((short)decoded);
    }

    /// <summary>Rejects bytes after a terminal block unless they form the documented 0x07 padding block.</summary>
    /// <param name="padding">Bytes following the terminal block.</param>
    private static void ValidateTerminalPadding(ReadOnlySpan<byte> padding)
    {
        if (padding.IsEmpty)
        {
            return;
        }

        if (padding.Length != 16 || padding[1] != 7)
        {
            throw new FormatException("SPU ADPCM terminal data has unsupported trailing blocks.");
        }
    }
}
