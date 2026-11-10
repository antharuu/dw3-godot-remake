using System.Buffers.Binary;
using Dw3.Formats.Battle;
using Xunit;

namespace Dw3.Formats.Tests.Battle;

/// <summary>Tests the bounded decoding of fighter action-script archive entries.</summary>
public sealed class ActionScriptDecoderTests
{
    // ── RED fixtures (must FAIL before the fix and PASS after) ───────────────

    /// <summary>Rejects a camera command whose mode word is missing (truncated to opcode + time only).</summary>
    [Fact]
    public void DecodeRejectsCameraTruncatedWithNoMode()
    {
        // [5, 0] = opcode Camera (5) then time=0; mode is missing → truncated FormatException.
        byte[] archive = Archive(Script(5, 0));

        FormatException error = Assert.Throws<FormatException>(() => BattleActionScriptDecoder.Decode(archive, 0));

        Assert.Contains("truncated", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Correctly segments a stage fade (mode 0) that also reads fade-out and fade-in words
    /// before continuing to the following model and end commands.
    /// The old Animate grammar misread [15,15] as a HitTrigger opcode.
    /// </summary>
    [Fact]
    public void DecodeStageMode0ConsumesStageIdAndFades()
    {
        // Raw: Stage(mode=0, stageId=56, fadeOut=15, fadeIn=15), Model(sub=0, actor=0, motion=7), End
        byte[] archive = Archive(Script(4, 0, 56, 15, 15, 2, 0, 0, 7, 0));

        BattleActionScript script = BattleActionScriptDecoder.Decode(archive, 0);

        Assert.Equal(3, script.Commands.Count);
        Assert.Equal(BattleActionOpcode.Stage, script.Commands[0].Opcode);
        Assert.Equal<short>([0, 56, 15, 15], script.Commands[0].Arguments);
        Assert.Equal(BattleActionOpcode.Model, script.Commands[1].Opcode);
        Assert.Equal<short>([0, 0, 7], script.Commands[1].Arguments);
        Assert.Equal(BattleActionOpcode.End, script.Commands[2].Opcode);
    }

    // ── Positive-control fixtures ────────────────────────────────────────────

    /// <summary>Decodes a full camera command with mode 0 and camera-id word.</summary>
    [Fact]
    public void DecodeCameraMode0ReadsTimeModAndId()
    {
        // [5, 15, 0, 2, 0] = Camera(time=15, mode=0, id=2), End
        byte[] archive = Archive(Script(5, 15, 0, 2, 0));

        BattleActionScript script = BattleActionScriptDecoder.Decode(archive, 0);

        Assert.Collection(
            script.Commands,
            command =>
            {
                Assert.Equal(BattleActionOpcode.Camera, command.Opcode);
                Assert.Equal<short>([15, 0, 2], command.Arguments);
            },
            command => Assert.Equal(BattleActionOpcode.End, command.Opcode));
    }

    /// <summary>Stage mode 1 (load) reads only mode and stage-id.</summary>
    [Fact]
    public void DecodeStageModeOneConsumesOnlyModeAndStageId()
    {
        // [4, 1, 56, 0] = Stage(mode=1, stageId=56), End
        byte[] archive = Archive(Script(4, 1, 56, 0));

        BattleActionScript script = BattleActionScriptDecoder.Decode(archive, 0);

        Assert.Collection(
            script.Commands,
            command =>
            {
                Assert.Equal(BattleActionOpcode.Stage, command.Opcode);
                Assert.Equal<short>([1, 56], command.Arguments);
            },
            command => Assert.Equal(BattleActionOpcode.End, command.Opcode));
    }

    /// <summary>Stage mode 0 and mode 1 in sequence without consuming adjacent opcodes.</summary>
    [Fact]
    public void DecodeStageLoadThenFadeInSequence()
    {
        // Stage(1,56) = load: 3 words; Stage(0,56,15,15) = fade: 5 words; End
        byte[] archive = Archive(Script(4, 1, 56, 4, 0, 56, 15, 15, 0));

        BattleActionScript script = BattleActionScriptDecoder.Decode(archive, 0);

        Assert.Equal(3, script.Commands.Count);
        Assert.Equal<short>([1, 56], script.Commands[0].Arguments);
        Assert.Equal<short>([0, 56, 15, 15], script.Commands[1].Arguments);
        Assert.Equal(BattleActionOpcode.End, script.Commands[2].Opcode);
    }

    // ── Existing format fixtures (opcodes preserved) ─────────────────────────

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

    /// <summary>Hiding a model preserves the following camera command and its complete operands.</summary>
    [Fact]
    public void DecodeModelHidePreservesFollowingCamera()
    {
        BattleActionScript script = BattleActionScriptDecoder.Decode(Archive(Script(2, 2, 4, 5, 9, 0, 2, 0)), 0);

        Assert.Collection(
            script.Commands,
            command =>
            {
                Assert.Equal(BattleActionOpcode.Model, command.Opcode);
                Assert.Equal<short>([2, 4], command.Arguments);
            },
            command =>
            {
                Assert.Equal(BattleActionOpcode.Camera, command.Opcode);
                Assert.Equal<short>([9, 0, 2], command.Arguments);
            },
            command => Assert.Equal(BattleActionOpcode.End, command.Opcode));
    }

    /// <summary>Consumes the complete effect position and rotation before the following wait command.</summary>
    [Fact]
    public void DecodeConsumesEffectModelLoadAndTrigger()
    {
        BattleActionScript script = BattleActionScriptDecoder.Decode(
            Archive(Script(6, 1, 79, 6, 0, 79, 0, 2, 3, 4, 5, 6, 11, 9, 0)), 0);

        Assert.Equal(4, script.Commands.Count);
        Assert.Equal(BattleActionOpcode.EffectModel, script.Commands[0].Opcode);
        Assert.Equal<short>([1, 79], script.Commands[0].Arguments);
        Assert.Equal(BattleActionOpcode.EffectModel, script.Commands[1].Opcode);
        Assert.Equal<short>([0, 79, 0, 2, 3, 4, 5, 6], script.Commands[1].Arguments);
        Assert.Equal(BattleActionOpcode.Wait, script.Commands[2].Opcode);
        Assert.Equal<short>([9], script.Commands[2].Arguments);
        Assert.Equal(BattleActionOpcode.End, script.Commands[3].Opcode);
    }

    /// <summary>Rejects each incomplete prefix of the effect's position and rotation.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void DecodeRejectsTruncatedEffectModelTransform(int coordinateCount)
    {
        short[] command = [6, 0, 79, 10, 20, 30, 40, 50, 60];
        byte[] archive = Archive(command[..(3 + coordinateCount)]);

        Assert.Throws<FormatException>(() => BattleActionScriptDecoder.Decode(archive, 0));
    }

    /// <summary>Rejects an explicit view whose coordinates and projection are incomplete.</summary>
    [Fact]
    public void DecodeRejectsTruncatedExplicitCamera()
    {
        byte[] archive = Archive(Script(5, 15, 1, 0));

        FormatException error = Assert.Throws<FormatException>(() => BattleActionScriptDecoder.Decode(archive, 0));

        Assert.Contains("truncated", error.Message, StringComparison.Ordinal);
    }

    /// <summary>Preserves adjacent resource, stage and motion commands with fabricated identifiers and timings.</summary>
    [Fact]
    public void DecodeReadsAttackScriptPattern()
    {
        byte[] archive = Archive(Script(3, 1, 77, 4, 1, 9, 4, 0, 9, 3, 4, 2, 0, 0, 8, 0));

        BattleActionScript script = BattleActionScriptDecoder.Decode(archive, 0);

        Assert.Equal(5, script.Commands.Count);
        Assert.Equal(BattleActionOpcode.SpriteEffect, script.Commands[0].Opcode);
        Assert.Equal(BattleActionOpcode.Stage, script.Commands[1].Opcode);
        Assert.Equal<short>([1, 9], script.Commands[1].Arguments);
        Assert.Equal(BattleActionOpcode.Stage, script.Commands[2].Opcode);
        Assert.Equal<short>([0, 9, 3, 4], script.Commands[2].Arguments);
        Assert.Equal(BattleActionOpcode.Model, script.Commands[3].Opcode);
        Assert.Equal<short>([0, 0, 8], script.Commands[3].Arguments);
        Assert.Equal(BattleActionOpcode.End, script.Commands[4].Opcode);
    }

    /// <summary>Preserves the sound reference, timing and following command boundary.</summary>
    [Fact]
    public void DecodeSoundKeepsFollowingWait()
    {
        BattleActionScript script = BattleActionScriptDecoder.Decode(Archive(Script(10, 23, 4, 11, 6, 0)), 0);

        Assert.Equal<short>([23, 4], script.Commands[0].Arguments);
        Assert.Equal(10, (int)script.Commands[0].Opcode);
        Assert.Equal(BattleActionOpcode.Wait, script.Commands[1].Opcode);
        Assert.Equal(BattleActionOpcode.End, script.Commands[2].Opcode);
    }

    /// <summary>Preserves the explicit camera view without consuming the following model command.</summary>
    [Fact]
    public void DecodeExplicitCameraKeepsAllViewFields()
    {
        short[] view = [.. Enumerable.Range(11, 14).Select(value => (short)value)];
        short[] words = [5, 9, 1, .. view, 2, 1, 0, 0];
        BattleActionScript script = BattleActionScriptDecoder.Decode(Archive(words), 0);

        Assert.Equal(BattleActionOpcode.Camera, script.Commands[0].Opcode);
        Assert.Equal<short>([9, 1, .. view], script.Commands[0].Arguments);
        Assert.Equal(BattleActionOpcode.Model, script.Commands[1].Opcode);
        Assert.Equal<short>([1, 0], script.Commands[1].Arguments);
        Assert.Equal(BattleActionOpcode.End, script.Commands[2].Opcode);
    }

    /// <summary>Preserves the hit selector and alternate terminator as distinct semantic commands.</summary>
    [Fact]
    public void DecodeHitDispatchKeepsAlternateTerminator()
    {
        BattleActionScript script = BattleActionScriptDecoder.Decode(Archive(Script(1, 5, 255)), 0);

        Assert.Equal(1, (int)script.Commands[0].Opcode);
        Assert.Equal<short>([5], script.Commands[0].Arguments);
        Assert.Equal(255, (int)script.Commands[1].Opcode);
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
