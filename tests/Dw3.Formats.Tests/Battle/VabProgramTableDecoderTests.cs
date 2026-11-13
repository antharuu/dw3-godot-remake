using System.Buffers.Binary;
using Dw3.Formats.Battle;
using Xunit;

namespace Dw3.Formats.Tests.Battle;

/// <summary>Synthetic contract tests for VAB program and tone table decoding.</summary>
public sealed class VabProgramTableDecoderTests
{
    [Fact]
    public void Decode_ReturnsNullForProgramsWithAllZeroPriorities()
    {
        byte[] vabEntry = BuildEntry(programCount: 2, programs:
        [
            BuildToneSlots(16, priority: 0),
            BuildToneSlots(16, priority: 0),
        ]);

        VabToneAttributes?[] result = VabProgramTableDecoder.Decode(vabEntry, programCount: 2);

        Assert.Null(result[0]);
        Assert.Null(result[1]);
    }

    [Fact]
    public void Decode_ReturnsFirstActiveToneForEachProgram()
    {
        var spec0 = new ToneSpec(Priority: 1, Volume: 110, Pan: 64, Center: 60, Shift: 0, MinNote: 0, MaxNote: 127, Prog: 1, SampleRef: 42) { PitchBendDown = 5, PitchBendUp = 7 };
        var spec1 = new ToneSpec(Priority: 2, Volume: 80, Pan: 32, Center: 48, Shift: -12, MinNote: 0, MaxNote: 127, Prog: 2, SampleRef: 7);
        byte[] vabEntry = BuildEntry(programCount: 2, programs:
        [
            BuildToneSlots(16, activeToneIndex: 0, activeTone: BuildTone(spec0)),
            BuildToneSlots(16, activeToneIndex: 1, activeTone: BuildTone(spec1)),
        ]);

        VabToneAttributes?[] result = VabProgramTableDecoder.Decode(vabEntry, programCount: 2);

        Assert.NotNull(result[0]);
        VabToneAttributes first = result[0]!;
        Assert.Equal(1, first.Priority);
        Assert.Equal(110, first.Volume);
        Assert.Equal(64, first.Pan);
        Assert.Equal(60, first.CenterNote);
        Assert.Equal(0, first.PitchShift);
        Assert.Equal(5, first.PitchBendDown);
        Assert.Equal(7, first.PitchBendUp);
        Assert.Equal(42, first.SampleRef);

        Assert.NotNull(result[1]);
        VabToneAttributes second = result[1]!;
        Assert.Equal(2, second.Priority);
        Assert.Equal(80, second.Volume);
        Assert.Equal(32, second.Pan);
        Assert.Equal(48, second.CenterNote);
        Assert.Equal(-12, second.PitchShift);
        Assert.Equal(7, second.SampleRef);
    }

    [Fact]
    public void Decode_PicksFirstNonZeroPrioritySlot()
    {
        var spec = new ToneSpec(Priority: 5, Volume: 100, Pan: 64, Center: 60, Shift: 0, MinNote: 0, MaxNote: 127, Prog: 1, SampleRef: 11);
        byte[] vabEntry = BuildEntry(programCount: 1, programs:
        [
            BuildToneSlots(16, activeToneIndex: 3, activeTone: BuildTone(spec)),
        ]);

        VabToneAttributes?[] result = VabProgramTableDecoder.Decode(vabEntry, programCount: 1);

        Assert.NotNull(result[0]);
        VabToneAttributes tone = result[0]!;
        Assert.Equal(5, tone.Priority);
        Assert.Equal(11, tone.SampleRef);
    }

    [Fact]
    public void DecodeAll_ReadsTheReverbModeByteOfEveryDeclaredTone()
    {
        var spec = new ToneSpec(Priority: 3, Volume: 100, Pan: 64, Center: 60, Shift: 0, MinNote: 0, MaxNote: 127, Prog: 0, SampleRef: 9);
        byte[] wet = BuildTone(spec);
        wet[1] = 4;
        byte[] dry = BuildTone(spec);
        byte[] slots = new byte[16 * 32];
        wet.CopyTo(slots, 0);
        dry.CopyTo(slots, 32);
        byte[] vabEntry = BuildEntry(programCount: 1, programs: [slots]);
        vabEntry[32] = 2;

        var tones = VabProgramTableDecoder.DecodeAll(vabEntry, programCount: 1);

        Assert.Equal([4, 0], tones.Select(tone => (int)tone.Attributes.Mode));
    }

    [Fact]
    public void Decode_RejectsEntriesTooSmallForDeclaredProgramCount()
    {
        byte[] tooSmall = new byte[32 + 2048 + 10];
        Assert.Throws<FormatException>(() => VabProgramTableDecoder.Decode(tooSmall, programCount: 4));
    }

    [Fact]
    public void Decode_NullForSlotsBeyondProgramCount()
    {
        var spec = new ToneSpec(Priority: 1, Volume: 90, Pan: 64, Center: 60, Shift: 0, MinNote: 0, MaxNote: 127, Prog: 1, SampleRef: 5);
        byte[] vabEntry = BuildEntry(programCount: 1, programs:
        [
            BuildToneSlots(16, activeToneIndex: 0, activeTone: BuildTone(spec)),
        ]);

        VabToneAttributes?[] result = VabProgramTableDecoder.Decode(vabEntry, programCount: 1);

        Assert.NotNull(result[0]);
        for (int i = 1; i < 128; i++)
        {
            Assert.Null(result[i]);
        }
    }

    private static byte[] BuildEntry(int programCount, byte[][] programs)
    {
        int toneBytes = programCount * 16 * 32;
        byte[] entry = new byte[32 + 2048 + toneBytes + 512];
        int toneOffset = 32 + 2048;
        for (int p = 0; p < programs.Length; p++)
        {
            programs[p].CopyTo(entry, toneOffset + (p * 16 * 32));
        }

        return entry;
    }

    private static byte[] BuildToneSlots(int slotCount, byte priority = 0, int activeToneIndex = 0, byte[]? activeTone = null)
    {
        byte[] slots = new byte[slotCount * 32];
        if (activeTone is not null)
        {
            activeTone.CopyTo(slots, activeToneIndex * 32);
        }
        else if (priority != 0)
        {
            slots[0] = priority;
        }

        return slots;
    }

    private static byte[] BuildTone(ToneSpec spec)
    {
        byte[] tone = new byte[32];
        tone[0] = spec.Priority;
        tone[2] = spec.Volume;
        tone[3] = spec.Pan;
        tone[4] = spec.Center;
        tone[5] = (byte)spec.Shift;
        tone[12] = spec.PitchBendDown;
        tone[13] = spec.PitchBendUp;
        tone[6] = spec.MinNote;
        tone[7] = spec.MaxNote;
        BinaryPrimitives.WriteInt16LittleEndian(tone.AsSpan(20, 2), spec.Prog);
        BinaryPrimitives.WriteInt16LittleEndian(tone.AsSpan(22, 2), spec.SampleRef);
        return tone;
    }

    /// <summary>Parameters for constructing a single synthetic VAB tone attribute entry in tests.</summary>
    private sealed record ToneSpec(
        byte Priority,
        byte Volume,
        byte Pan,
        byte Center,
        sbyte Shift,
        byte MinNote,
        byte MaxNote,
        short Prog,
        short SampleRef)
    {
        public byte PitchBendDown { get; init; }

        public byte PitchBendUp { get; init; }
    }
}
