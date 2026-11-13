using System.Buffers.Binary;

namespace Dw3Production.Presentation;

/// <summary>Measures what a recorded excitation leaves after its own dry signal has ended.</summary>
/// <param name="OnsetFrame">First stereo frame carrying any signal, or -1 when the recording is silent.</param>
/// <param name="TailStartFrame">First frame after the dry excitation and its safety margin.</param>
/// <param name="TailPeak">Largest absolute sample in the tail window.</param>
/// <param name="TailEnergy">Sum of squared normalized samples in the tail window.</param>
/// <param name="TailFrames">Frames from the tail start to the last nonzero frame, zero for a silent tail.</param>
public readonly record struct ReverbTailMeasurement(int OnsetFrame, int TailStartFrame, int TailPeak, double TailEnergy, int TailFrames)
{
    /// <summary>Analyzes interleaved stereo signed 16-bit PCM.</summary>
    /// <param name="pcm">Recorded little-endian stereo PCM16.</param>
    /// <param name="dryFrames">Excitation length plus margin, counted from the onset.</param>
    /// <returns>The tail measurement.</returns>
    public static ReverbTailMeasurement Measure(ReadOnlySpan<byte> pcm, int dryFrames)
    {
        int frames = pcm.Length / 4;
        int onset = -1;
        for (int frame = 0; frame < frames && onset < 0; frame++)
        {
            if (Sample(pcm, frame * 2) != 0 || Sample(pcm, (frame * 2) + 1) != 0)
            {
                onset = frame;
            }
        }

        if (onset < 0)
        {
            return new ReverbTailMeasurement(-1, frames, 0, 0, 0);
        }

        int start = Math.Min(frames, onset + dryFrames);
        int peak = 0;
        double energy = 0;
        int last = start;
        for (int index = start * 2; index < frames * 2; index++)
        {
            int value = Sample(pcm, index);
            peak = Math.Max(peak, Math.Abs(value));
            energy += (value / 32768.0) * (value / 32768.0);
            last = value == 0 ? last : (index / 2) + 1;
        }

        return new ReverbTailMeasurement(onset, start, peak, energy, last - start);
    }

    /// <summary>Reads one signed sample.</summary>
    /// <param name="pcm">Interleaved PCM16.</param>
    /// <param name="index">Sample index across both channels.</param>
    /// <returns>Signed sample value.</returns>
    private static int Sample(ReadOnlySpan<byte> pcm, int index) => BinaryPrimitives.ReadInt16LittleEndian(pcm.Slice(index * 2, 2));
}
