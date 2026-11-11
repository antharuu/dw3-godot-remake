using System.Buffers.Binary;
using Dw3.Formats.Battle;
using Xunit;

namespace Dw3.Formats.Tests.Battle;

public sealed class VabToneCatalogTests
{
    [Fact]
    public void DecodeAll_SparsePhysicalProgram_ZeroPriorityIsStillAPlayableTone()
    {
        byte[] bytes = new byte[32 + 2048 + 512 + 512];
        bytes[32 + (60 * 16)] = 1;
        bytes[33 + (60 * 16)] = 100;
        bytes[36 + (60 * 16)] = 40;
        bytes[2082] = 90;
        bytes[2083] = 30;
        bytes[2084] = 60;
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(2102), 3);
        var tone = Assert.Single(VabProgramTableDecoder.DecodeAll(bytes, 1));
        Assert.Equal(60, tone.ProgramIndex);
        Assert.Equal(0, tone.ToneIndex);
        Assert.Equal(0, tone.Attributes.Priority);
        Assert.Equal(3, tone.Attributes.SampleRef);
        Assert.Equal(100, tone.ProgramVolume);
    }
}
