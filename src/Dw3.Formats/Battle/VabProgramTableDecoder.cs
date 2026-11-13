using System.Buffers.Binary;

namespace Dw3.Formats.Battle;

/// <summary>Decodes the PS1 VAB program and tone attribute tables from one VAB archive entry.
/// The layout is: 32-byte header, 128 program attributes (128 × 16 bytes = 2048 bytes),
/// ProgramCount × 16 tone attributes (each 32 bytes), 256 sample length slots (512 bytes).</summary>
public static class VabProgramTableDecoder
{
    private const int HeaderBytes = 32;
    private const int ProgramSlots = 128;
    private const int ProgramAttributeBytes = 16;
    private const int ProgramTableBytes = ProgramSlots * ProgramAttributeBytes;
    private const int ToneSlots = 16;
    private const int ToneAttributeBytes = 32;
    private const int SampleLengthTableBytes = 512;
    private const int ToneVolume = 2;
    private const int TonePan = 3;
    private const int ToneCenterNote = 4;
    private const int TonePitchShift = 5;
    private const int ToneMinNote = 6;
    private const int ToneMaxNote = 7;
    private const int ToneProgramRef = 20;
    private const int ToneSampleRef = 22;

    /// <summary>Decodes the first active tone for each program index up to ProgramCount.
    /// Programs beyond ProgramCount or with no active tones return null.</summary>
    /// <param name="vabEntry">Raw VAB entry bytes starting with the 32-byte VAB header.</param>
    /// <param name="programCount">Number of filled programs declared by the VAB header.</param>
    /// <returns>Array of length ProgramSlots; null slots have no active tone.</returns>
    /// <exception cref="FormatException">The entry is too small to hold the declared tables.</exception>
    public static VabToneAttributes?[] Decode(ReadOnlySpan<byte> vabEntry, ushort programCount)
    {
        int toneTableBytes = programCount * ToneSlots * ToneAttributeBytes;
        int minSize = HeaderBytes + ProgramTableBytes + toneTableBytes + SampleLengthTableBytes;
        if (vabEntry.Length < minSize)
        {
            throw new FormatException(
                $"VAB entry is {vabEntry.Length} bytes; expected at least {minSize} for {programCount} programs.");
        }

        var result = new VabToneAttributes?[ProgramSlots];
        ReadOnlySpan<byte> toneTable = vabEntry.Slice(HeaderBytes + ProgramTableBytes, toneTableBytes);

        for (int programIndex = 0; programIndex < programCount; programIndex++)
        {
            result[programIndex] = ReadFirstActiveTone(toneTable, programIndex);
        }

        return result;
    }

    /// <summary>Decodes populated physical programs using their tone counts and compact tone rows.
    /// Priority is an arbitration attribute, not a presence flag.</summary>
    /// <param name="vabEntry">Complete VAB header entry.</param>
    /// <param name="programCount">Declared populated program count.</param>
    /// <returns>All explicitly declared tones with physical indices.</returns>
    public static IReadOnlyList<VabProgramTone> DecodeAll(ReadOnlySpan<byte> vabEntry, ushort programCount)
    {
        int required = HeaderBytes + ProgramTableBytes + (programCount * ToneSlots * ToneAttributeBytes) + SampleLengthTableBytes;
        if (vabEntry.Length < required || programCount > ProgramSlots)
        {
            throw new FormatException("VAB program tables are truncated or invalid.");
        }

        var result = new List<VabProgramTone>();
        int compact = 0;
        for (int program = 0; program < ProgramSlots; program++)
        {
            var attributes = vabEntry.Slice(HeaderBytes + (program * ProgramAttributeBytes), ProgramAttributeBytes);
            int count = attributes[0];
            if (count == 0)
            {
                continue;
            }

            if (count > ToneSlots || compact >= programCount)
            {
                throw new FormatException("VAB declared program/tone count is inconsistent.");
            }

            int offset = HeaderBytes + ProgramTableBytes + (compact++ * ToneSlots * ToneAttributeBytes);
            for (int tone = 0; tone < count; tone++)
            {
                result.Add(new VabProgramTone(program, tone, attributes[1], attributes[4], ReadTone(vabEntry.Slice(offset + (tone * ToneAttributeBytes), ToneAttributeBytes))));
            }
        }

        if (compact != programCount)
        {
            throw new FormatException("VAB populated program count does not match header.");
        }

        return result;
    }

    /// <summary>Reads a declared tone independently of its priority.</summary>
    /// <param name="tone">One 32-byte tone.</param>
    /// <returns>Decoded parameters.</returns>
    private static VabToneAttributes ReadTone(ReadOnlySpan<byte> tone) => new(
        tone[0],
        tone[ToneVolume],
        tone[TonePan],
        tone[ToneCenterNote],
        (sbyte)tone[TonePitchShift],
        tone[ToneMinNote],
        tone[ToneMaxNote],
        BinaryPrimitives.ReadInt16LittleEndian(tone.Slice(ToneProgramRef, 2)),
        BinaryPrimitives.ReadInt16LittleEndian(tone.Slice(ToneSampleRef, 2)))
    {
        Adsr1 = BinaryPrimitives.ReadUInt16LittleEndian(tone.Slice(16, 2)),
        Adsr2 = BinaryPrimitives.ReadUInt16LittleEndian(tone.Slice(18, 2)),
    };

    /// <summary>Returns the first tone in the program's sixteen slots whose priority is non-zero, or null.</summary>
    /// <param name="toneTable">The full compacted tone table (programCount × 16 × 32 bytes).</param>
    /// <param name="programIndex">Zero-based program index.</param>
    /// <returns>The first active tone, or null when all sixteen slots have zero priority.</returns>
    private static VabToneAttributes? ReadFirstActiveTone(ReadOnlySpan<byte> toneTable, int programIndex)
    {
        int programOffset = programIndex * ToneSlots * ToneAttributeBytes;
        for (int toneSlot = 0; toneSlot < ToneSlots; toneSlot++)
        {
            int toneOffset = programOffset + (toneSlot * ToneAttributeBytes);
            ReadOnlySpan<byte> tone = toneTable.Slice(toneOffset, ToneAttributeBytes);
            byte priority = tone[0];
            if (priority == 0)
            {
                continue;
            }

            return new VabToneAttributes(
                Priority: priority,
                Volume: tone[ToneVolume],
                Pan: tone[TonePan],
                CenterNote: tone[ToneCenterNote],
                PitchShift: (sbyte)tone[TonePitchShift],
                MinNote: tone[ToneMinNote],
                MaxNote: tone[ToneMaxNote],
                ProgramRef: BinaryPrimitives.ReadInt16LittleEndian(tone.Slice(ToneProgramRef, 2)),
                SampleRef: BinaryPrimitives.ReadInt16LittleEndian(tone.Slice(ToneSampleRef, 2)));
        }

        return null;
    }
}
