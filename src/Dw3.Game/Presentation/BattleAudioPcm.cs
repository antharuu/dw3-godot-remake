using System.Buffers.Binary;

namespace Dw3Production.Presentation;

/// <summary>Applies a prepared mono sample's static linear pan before engine mixing.</summary>
internal static class BattleAudioPcm
{
    /// <summary>Converts mono PCM16 into stereo with independent left and right pan gains.</summary>
    /// <param name="mono">Little-endian mono frames.</param>
    /// <param name="pan">Combined pan, negative left and positive right.</param>
    /// <returns>Interleaved PCM16 consumed by the physical engine stream.</returns>
    public static byte[] Pan(byte[] mono, float pan)
    {
        if (mono.Length % 2 != 0)
        {
            throw new FormatException("Battle PCM16 has a truncated frame.");
        }

        pan = Math.Clamp(pan, -1, 1);
        float left = Math.Min(1, 1 - pan);
        float right = Math.Min(1, 1 + pan);
        var stereo = new byte[mono.Length * 2];
        for (int offset = 0; offset < mono.Length; offset += 2)
        {
            short value = BinaryPrimitives.ReadInt16LittleEndian(mono.AsSpan(offset, 2));
            BinaryPrimitives.WriteInt16LittleEndian(stereo.AsSpan(offset * 2, 2), (short)(value * left));
            BinaryPrimitives.WriteInt16LittleEndian(stereo.AsSpan((offset * 2) + 2, 2), (short)(value * right));
        }

        return stereo;
    }
}
