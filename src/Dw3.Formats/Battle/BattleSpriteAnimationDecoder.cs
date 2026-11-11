using System.Buffers.Binary;
using System.Numerics;

namespace Dw3.Formats.Battle;

/// <summary>Expands bounded sprite animation channels into source-time samples.</summary>
public static class BattleSpriteAnimationDecoder
{
    /// <summary>Decodes the nine channels of each normalized source-time sample.</summary>
    /// <param name="data">Bounded animation entry.</param>
    /// <returns>Frame, palette, offsets, scales and rotations for each sample.</returns>
    public static IReadOnlyList<short[]> Decode(ReadOnlySpan<byte> data)
    {
        if (data.Length < 24)
        {
            throw new FormatException("Sprite animation header is truncated.");
        }

        int flags = Word(data, 0);
        int stride = Word(data, 1);
        int duration = Word(data, 2);
        if ((flags & ~0x1FF) != 0 || stride != BitOperations.PopCount((uint)flags) || duration <= 0)
        {
            throw new FormatException("Sprite animation flags, stride or duration is invalid.");
        }

        if (data.Length < 24 + (duration * stride * 2))
        {
            throw new FormatException("Sprite animation samples are truncated.");
        }

        var frames = new List<short[]>(duration);
        for (int time = 0; time < duration; time++)
        {
            frames.Add(ReadSample(data, flags, stride, time));
        }

        return frames;
    }

    /// <summary>Reads one sample while preserving unselected initial channels.</summary>
    /// <param name="data">Validated animation entry.</param>
    /// <param name="flags">Nine selected channel bits.</param>
    /// <param name="stride">Words per sample.</param>
    /// <param name="time">Sample index.</param>
    /// <returns>Expanded channel values.</returns>
    private static short[] ReadSample(ReadOnlySpan<byte> data, int flags, int stride, int time)
    {
        var values = new short[9];
        int cursor = 12 + (time * stride);
        for (int channel = 0; channel < values.Length; channel++)
        {
            values[channel] = Word(data, (flags & (1 << channel)) == 0 ? 3 + channel : cursor++);
        }

        return values;
    }

    /// <summary>Reads a validated signed channel word.</summary>
    /// <param name="data">Entry bytes.</param>
    /// <param name="index">Word index.</param>
    /// <returns>Signed channel value.</returns>
    private static short Word(ReadOnlySpan<byte> data, int index) => BinaryPrimitives.ReadInt16LittleEndian(data[(index * 2)..]);
}
