using Dw3.Formats.Field;
using Xunit;

namespace Dw3.Formats.Tests.Dialogue;

public sealed class ProgressConditionDecoderTests
{
    [Fact]
    public void CountsRecordsWithTerminatorAndKeepsOnlyRangeChecks()
    {
        byte[] exe = new byte[32];
        exe[0] = 4; exe[1] = 0x31; exe[2] = 1;   // range check -> range 1
        exe[3] = 5; exe[4] = 0x20; exe[5] = 9;   // other family, argument not a range index
        exe[6] = 0xFF;                           // terminator
        exe[20] = 1; exe[21] = 3; exe[22] = 6; exe[23] = 8;

        ProgressConditionTables tables = ProgressConditionDecoder.Decode(exe, 20, 2, 0, 20);

        Assert.Equal(3, tables.SpecialRecordCount);
        Assert.Equal([new NpcProgressRange(1, 3), new NpcProgressRange(6, 8)], tables.Ranges);
        Assert.Equal(1, Assert.Single(tables.SpecialProgressRanges, pair => pair.Key == 4).Value);
        Assert.False(tables.SpecialProgressRanges.ContainsKey(5));
    }

    [Fact]
    public void RejectsMissingTerminator()
    {
        byte[] exe = new byte[32];
        exe[0] = 4; exe[1] = 0x20;

        Assert.Throws<FormatException>(() => ProgressConditionDecoder.Decode(exe, 20, 2, 0, 9));
    }

    [Fact]
    public void RejectsRangeCheckBeyondTheRangeTable()
    {
        byte[] exe = new byte[32];
        exe[0] = 4; exe[1] = 0x30; exe[2] = 2; exe[3] = 0xFF;

        Assert.Throws<FormatException>(() => ProgressConditionDecoder.Decode(exe, 20, 2, 0, 20));
    }

    [Fact]
    public void RejectsRangeTableOutsideTheExecutable() =>
        Assert.Throws<FormatException>(() => ProgressConditionDecoder.Decode(new byte[32], 30, 2, 0, 20));
}
