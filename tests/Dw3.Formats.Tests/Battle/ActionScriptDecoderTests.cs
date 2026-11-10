using System.Buffers.Binary;
using Dw3.Formats.Battle;
using Xunit;

namespace Dw3.Formats.Tests.Battle;

/// <summary>Tests the bounded decoding of fighter action-script archive entries.</summary>
public sealed class ActionScriptDecoderTests
{
    /// <summary>Decodes the ordinary motion, sprite-effect, wait and terminator action sequence.</summary>
    [Fact]
    public void DecodeReadsOnePhysicalTechniqueScript()
    {
        byte[] archive = Archive(
            Script(2, 0, 0, 3, 3, 0, 9999, 1, 2, 3, 11, 5, 0),
            Script(2, 0, 0, 7, 0));

        BattleActionScript script = BattleActionScriptDecoder.Decode(archive, 0);

        Assert.Equal(0, script.Entry);
        Assert.Equal(4, script.Commands.Count);
        Assert.Equal(BattleActionOpcode.Model, script.Commands[0].Opcode);
        Assert.Equal([0, 0, 3], script.Commands[0].Arguments);
        Assert.Equal(BattleActionOpcode.SpriteEffect, script.Commands[1].Opcode);
        Assert.Equal([0, 9999, 1, 2, 3], script.Commands[1].Arguments);
        Assert.Equal(BattleActionOpcode.Wait, script.Commands[2].Opcode);
        Assert.Equal([5], script.Commands[2].Arguments);
        Assert.Equal(BattleActionOpcode.End, script.Commands[3].Opcode);
    }

    /// <summary>Rejects a script whose declared command extends beyond the selected archive entry.</summary>
    [Fact]
    public void DecodeRejectsTruncatedSpriteEffect()
    {
        byte[] archive = Archive(Script(3, 0, 2));

        FormatException error = Assert.Throws<FormatException>(() => BattleActionScriptDecoder.Decode(archive, 0));

        Assert.Equal("battle action script entry 0 command 0x3 is truncated", error.Message);
    }

    /// <summary>Consumes the motion number selected by model subcommand zero before reading the end opcode.</summary>
    [Fact]
    public void DecodeConsumesMotionNumberForModelSubcommandZero()
    {
        BattleActionScript script = BattleActionScriptDecoder.Decode(Archive(Script(2, 0, 0, 7, 0)), 0);

        Assert.Collection(
            script.Commands,
            command =>
            {
                Assert.Equal(BattleActionOpcode.Model, command.Opcode);
                Assert.Equal([0, 0, 7], command.Arguments);
            },
            command => Assert.Equal(BattleActionOpcode.End, command.Opcode));
    }

    /// <summary>Does not require coordinates for the sprite-effect loading subcommand.</summary>
    [Fact]
    public void DecodeConsumesOnlyLoadArgumentsForSpriteEffectSubcommandOne()
    {
        BattleActionScript script = BattleActionScriptDecoder.Decode(Archive(Script(3, 1, 9999, 0)), 0);

        Assert.Collection(
            script.Commands,
            command =>
            {
                Assert.Equal(BattleActionOpcode.SpriteEffect, command.Opcode);
                Assert.Equal([1, 9999], command.Arguments);
            },
            command => Assert.Equal(BattleActionOpcode.End, command.Opcode));
    }

    /// <summary>Consumes the model visibility control without treating the terminator as an argument.</summary>
    [Fact]
    public void DecodeConsumesVisibilityControlForModelSubcommandOne()
    {
        BattleActionScript script = BattleActionScriptDecoder.Decode(Archive(Script(2, 1, 0, 0)), 0);

        Assert.Collection(
            script.Commands,
            command =>
            {
                Assert.Equal(BattleActionOpcode.Model, command.Opcode);
                Assert.Equal([1, 0], command.Arguments);
            },
            command => Assert.Equal(BattleActionOpcode.End, command.Opcode));
    }

    /// <summary>Builds a u32-offset archive from independently supplied s16 script entries.</summary>
    private static byte[] Archive(params short[][] entries)
    {
        int tableBytes = entries.Length * 4;
        int length = tableBytes + entries.Sum(entry => entry.Length * 2);
        var bytes = new byte[length];
        int offset = tableBytes;
        for (int index = 0; index < entries.Length; index++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(index * 4, 4), (uint)offset);
            foreach (short value in entries[index])
            {
                BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(offset, 2), value);
                offset += 2;
            }
        }

        return bytes;
    }

    /// <summary>Returns one script literal.</summary>
    private static short[] Script(params short[] values) => values;
}
