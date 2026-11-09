using System.Buffers.Binary;

namespace Dw3.Formats.Battle;

/// <summary>Writes mono signed-16-bit PCM samples as a canonical RIFF/WAVE file.</summary>
public static class WavePcm16Encoder
{
    private const int HeaderBytes = 44;
    private const ushort Mono = 1;
    private const ushort BitsPerSample = 16;

    /// <summary>Encodes one mono PCM stream at the supplied sample rate.</summary>
    /// <param name="samples">Signed PCM samples.</param>
    /// <param name="sampleRate">Output sample rate in hertz.</param>
    /// <returns>A complete little-endian RIFF/WAVE byte stream.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The sample rate is not positive.</exception>
    public static byte[] EncodeMono(ReadOnlySpan<short> samples, int sampleRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        int dataBytes = checked(samples.Length * sizeof(short));
        byte[] wave = new byte[checked(HeaderBytes + dataBytes)];
        WriteHeader(wave, dataBytes, sampleRate);
        WriteSamples(wave.AsSpan(HeaderBytes), samples);
        return wave;
    }

    /// <summary>Writes the fixed PCM RIFF and fmt chunks.</summary>
    /// <param name="wave">Complete output buffer.</param>
    /// <param name="dataBytes">PCM payload byte count.</param>
    /// <param name="sampleRate">Output sample rate in hertz.</param>
    private static void WriteHeader(Span<byte> wave, int dataBytes, int sampleRate)
    {
        "RIFF"u8.CopyTo(wave);
        BinaryPrimitives.WriteInt32LittleEndian(wave.Slice(4), checked(36 + dataBytes));
        "WAVEfmt "u8.CopyTo(wave.Slice(8));
        BinaryPrimitives.WriteInt32LittleEndian(wave.Slice(16), 16);
        BinaryPrimitives.WriteUInt16LittleEndian(wave.Slice(20), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(wave.Slice(22), Mono);
        BinaryPrimitives.WriteInt32LittleEndian(wave.Slice(24), sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(wave.Slice(28), checked(sampleRate * Mono * BitsPerSample / 8));
        BinaryPrimitives.WriteUInt16LittleEndian(wave.Slice(32), Mono * BitsPerSample / 8);
        BinaryPrimitives.WriteUInt16LittleEndian(wave.Slice(34), BitsPerSample);
        "data"u8.CopyTo(wave.Slice(36));
        BinaryPrimitives.WriteInt32LittleEndian(wave.Slice(40), dataBytes);
    }

    /// <summary>Writes each sample in little-endian signed-16-bit form.</summary>
    /// <param name="destination">PCM payload destination.</param>
    /// <param name="samples">Source PCM samples.</param>
    private static void WriteSamples(Span<byte> destination, ReadOnlySpan<short> samples)
    {
        for (int index = 0; index < samples.Length; index++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(destination.Slice(index * sizeof(short), sizeof(short)), samples[index]);
        }
    }
}
