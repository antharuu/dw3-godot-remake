using System.Buffers.Binary;

namespace Dw3.Formats.Battle;

/// <summary>Extracts the supported battle entrance's direct cue from its call-site operands.</summary>
public static class BattleEntranceCueDecoder
{
    private const int CueOperandOffset = 0x40C8;

    /// <summary>Reads the packed direct key-on without confusing it with an action-table index.</summary>
    /// <param name="overlay">Supported user-disc battle overlay.</param>
    /// <returns>The encoded cue actually passed to the sound call.</returns>
    public static uint Decode(ReadOnlySpan<byte> overlay)
    {
        if (overlay.Length < CueOperandOffset + 4)
        {
            throw new FormatException("Battle entrance sound call is truncated.");
        }

        uint low = BinaryPrimitives.ReadUInt32LittleEndian(overlay[CueOperandOffset..]);
        uint call = BinaryPrimitives.ReadUInt32LittleEndian(overlay[(CueOperandOffset - 4)..]);
        if ((low >> 16) != 0x3484 || call != 0x0040F809)
        {
            throw new FormatException("Unsupported battle entrance sound call operands.");
        }

        for (int offset = CueOperandOffset - 8; offset >= CueOperandOffset - 96; offset -= 4)
        {
            uint instruction = BinaryPrimitives.ReadUInt32LittleEndian(overlay[offset..]);
            if ((instruction >> 16) == 0x3C04)
            {
                return ((instruction & 65535) << 16) | (low & 65535);
            }
        }

        throw new FormatException("Battle entrance sound has no bounded upper operand.");
    }
}
