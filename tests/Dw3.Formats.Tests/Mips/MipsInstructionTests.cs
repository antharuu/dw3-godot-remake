using Dw3.Formats.Mips;
using Xunit;
using static Dw3.Formats.Tests.Mips.MipsAssembler;

namespace Dw3.Formats.Tests.Mips;

public sealed class MipsInstructionTests
{
    [Fact]
    public void DecodesImmediateFieldsWithSignExtension()
    {
        MipsInstruction instruction = new(0x10, Addiu(T0, S0, -4));

        Assert.Equal(MipsOperation.Addiu, instruction.Operation);
        Assert.Equal(S0, instruction.Rs);
        Assert.Equal(T0, instruction.Rt);
        Assert.Equal(-4, instruction.SignedImmediate);
        Assert.Equal(0xFFFC, instruction.UnsignedImmediate);
        Assert.Equal(0x10, instruction.Offset);
    }

    [Theory]
    [InlineData(0x3C080000u, MipsOperation.Lui)]
    [InlineData(0x35080000u, MipsOperation.Ori)]
    [InlineData(0x8D080000u, MipsOperation.Lw)]
    [InlineData(0xAD080000u, MipsOperation.Sw)]
    [InlineData(0x0C000000u, MipsOperation.Jal)]
    [InlineData(0x03E00008u, MipsOperation.Jr)]
    [InlineData(0x01094021u, MipsOperation.Addu)]
    [InlineData(0x01094025u, MipsOperation.Or)]
    [InlineData(0x91080000u, MipsOperation.Other)]
    public void ClassifiesSupportedOperations(uint word, MipsOperation expected) =>
        Assert.Equal(expected, new MipsInstruction(0, word).Operation);

    [Fact]
    public void DecodesJumpTargetInsideCurrentSegment()
    {
        MipsInstruction call = new(0, Jal(0x80012345 & ~3u));

        Assert.Equal(0x80012344u, call.JumpTarget(0x800A5DE0u));
    }

    [Fact]
    public void RegisterDestinationFollowsInstructionFormat()
    {
        Assert.Equal(T0, new MipsInstruction(0, Lw(T0, S0, 4)).Destination);
        Assert.Equal(V0, new MipsInstruction(0, Addu(V0, A0, A1)).Destination);
        Assert.Equal(Ra, new MipsInstruction(0, Jal(0x80010000)).Destination);
        Assert.Null(new MipsInstruction(0, Sw(T0, S0, 4)).Destination);
        Assert.Null(new MipsInstruction(0, Jr(Ra)).Destination);
        Assert.Null(new MipsInstruction(0, J(0x80010000)).Destination);
    }

    [Fact]
    public void DecodeRangeReadsLittleEndianWordsWithOffsets()
    {
        byte[] code = Code(Nop, Lui(T0, 0x80090000), Jr(Ra));

        MipsInstruction[] instructions = MipsInstruction.DecodeRange(code, 4, 12);

        Assert.Equal([4, 8], instructions.Select(instruction => instruction.Offset));
        Assert.Equal(MipsOperation.Lui, instructions[0].Operation);
        Assert.Equal(MipsOperation.Jr, instructions[1].Operation);
    }

    [Theory]
    [InlineData(2, 8)]
    [InlineData(0, 10)]
    [InlineData(0, 16)]
    [InlineData(8, 4)]
    [InlineData(-4, 8)]
    public void DecodeRangeRejectsUnalignedOrOutOfRangeWindows(int start, int end)
    {
        byte[] code = Code(Nop, Nop, Nop);

        Assert.Throws<MipsDecodeException>(() => MipsInstruction.DecodeRange(code, start, end));
    }

    [Fact]
    public void WholeWordEndExcludesTrailingDataBytes()
    {
        Assert.Equal(20, MipsInstruction.WholeWordEnd(23));
        Assert.Equal(0, MipsInstruction.WholeWordEnd(3));
    }
}
