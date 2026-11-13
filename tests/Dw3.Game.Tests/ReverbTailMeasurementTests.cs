using System.Buffers.Binary;
using Dw3Production.Presentation;
using Xunit;

namespace Dw3.Game.Tests;

public sealed class ReverbTailMeasurementTests
{
    [Fact]
    public void Measure_DryBurstOnly_HasASilentTail()
    {
        byte[] pcm = Stereo(0, 0, 100, 100, 0, 0, 0);

        var result = ReverbTailMeasurement.Measure(pcm, 2);

        Assert.Equal(2, result.OnsetFrame);
        Assert.Equal(0, result.TailPeak);
        Assert.Equal(0, result.TailFrames);
    }

    [Fact]
    public void Measure_SignalAfterTheDryWindow_IsReportedAsTail()
    {
        byte[] pcm = Stereo(0, 100, 100, -30, 0, 10, 0);

        var result = ReverbTailMeasurement.Measure(pcm, 2);

        Assert.Equal(1, result.OnsetFrame);
        Assert.Equal(3, result.TailStartFrame);
        Assert.Equal(30, result.TailPeak);
        Assert.Equal(3, result.TailFrames);
        Assert.True(result.TailEnergy > 0);
    }

    [Fact]
    public void Measure_SilentRecording_HasNoOnset()
    {
        var result = ReverbTailMeasurement.Measure(Stereo(0, 0, 0), 1);

        Assert.Equal(-1, result.OnsetFrame);
        Assert.Equal(0, result.TailPeak);
    }

    private static byte[] Stereo(params short[] left)
    {
        byte[] pcm = new byte[left.Length * 4];
        for (int frame = 0; frame < left.Length; frame++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(frame * 4, 2), left[frame]);
            BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan((frame * 4) + 2, 2), left[frame]);
        }

        return pcm;
    }
}
