using System.Buffers.Binary;
using Dw3.Formats.Battle;
using Xunit;

namespace Dw3.Formats.Tests.Battle;

public sealed class BattleEntranceCueDecoderTests
{
    [Fact]
    public void Decode_ReadsTheDirectCueRatherThanAnActionCatalogIndex()
    {
        var data = new byte[0x40D0];
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x40B4), 0x3C04A004);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x40C4), 0x0040F809);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x40C8), 0x348412B6);
        Assert.Equal(0xA00412B6U, BattleEntranceCueDecoder.Decode(data));
        data[0x40C4] = 0;
        Assert.Throws<FormatException>(() => BattleEntranceCueDecoder.Decode(data));
        Assert.Throws<FormatException>(() => BattleEntranceCueDecoder.Decode([]));
    }
}
