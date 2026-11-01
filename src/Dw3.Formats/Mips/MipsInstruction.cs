using System.Buffers.Binary;

namespace Dw3.Formats.Mips;

/// <summary>The R3000 operations the overlay analysis understands. Everything else is <see cref="Other"/>.</summary>
public enum MipsOperation
{
    /// <summary>Any instruction the analysis does not interpret.</summary>
    Other,
    /// <summary><c>lui rt, imm</c>: loads the immediate into the upper half.</summary>
    Lui,
    /// <summary><c>addiu rt, rs, imm</c>: adds the sign-extended immediate.</summary>
    Addiu,
    /// <summary><c>ori rt, rs, imm</c>: ORs the zero-extended immediate.</summary>
    Ori,
    /// <summary><c>lw rt, imm(rs)</c>: loads a 32-bit word.</summary>
    Lw,
    /// <summary><c>sw rt, imm(rs)</c>: stores a 32-bit word.</summary>
    Sw,
    /// <summary><c>j target</c>: jumps inside the current 256 MiB segment.</summary>
    J,
    /// <summary><c>jal target</c>: calls inside the current 256 MiB segment, writes <c>$ra</c>.</summary>
    Jal,
    /// <summary><c>jr rs</c>: jumps to a register, a function return when <c>rs</c> is <c>$ra</c>.</summary>
    Jr,
    /// <summary><c>jalr rd, rs</c>: calls through a register.</summary>
    Jalr,
    /// <summary><c>addu rd, rs, rt</c>: 32-bit addition without overflow trap.</summary>
    Addu,
    /// <summary><c>or rd, rs, rt</c>: bitwise OR, the usual register copy idiom with <c>$zero</c>.</summary>
    Or,
}

/// <summary>
/// One little-endian 32-bit R3000 instruction word at a byte offset of a code buffer.
/// Field extraction follows the three MIPS I encodings (I-type, J-type and R-type).
/// </summary>
public readonly record struct MipsInstruction(int Offset, uint Word)
{
    /// <summary>Primary opcode, bits 31..26.</summary>
    public int Opcode => (int)(Word >> 26);
    /// <summary>First source register, bits 25..21.</summary>
    public int Rs => (int)((Word >> 21) & 31);
    /// <summary>Second source or I-type destination register, bits 20..16.</summary>
    public int Rt => (int)((Word >> 16) & 31);
    /// <summary>R-type destination register, bits 15..11.</summary>
    public int Rd => (int)((Word >> 11) & 31);
    /// <summary>R-type function code, bits 5..0.</summary>
    public int Function => (int)(Word & 63);
    /// <summary>Zero-extended 16-bit immediate.</summary>
    public ushort UnsignedImmediate => (ushort)Word;
    /// <summary>Sign-extended 16-bit immediate (also the load/store displacement).</summary>
    public short SignedImmediate => (short)Word;

    /// <summary>The recognised operation, or <see cref="MipsOperation.Other"/>.</summary>
    public MipsOperation Operation => Opcode switch
    {
        0x00 => Function switch
        {
            0x08 => MipsOperation.Jr,
            0x09 => MipsOperation.Jalr,
            0x21 => MipsOperation.Addu,
            0x25 => MipsOperation.Or,
            _ => MipsOperation.Other,
        },
        0x02 => MipsOperation.J,
        0x03 => MipsOperation.Jal,
        0x09 => MipsOperation.Addiu,
        0x0D => MipsOperation.Ori,
        0x0F => MipsOperation.Lui,
        0x23 => MipsOperation.Lw,
        0x2B => MipsOperation.Sw,
        _ => MipsOperation.Other,
    };

    /// <summary>True when the following word executes in this instruction's branch delay slot.</summary>
    public bool HasDelaySlot => Opcode switch
    {
        0x00 => Function is 0x08 or 0x09,
        // REGIMM branches, j, jal, beq, bne, blez, bgtz.
        0x01 or 0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 => true,
        _ => false,
    };

    /// <summary>
    /// The general-purpose register this instruction writes, or null when it writes none.
    /// Coprocessor moves to the CPU (<c>mfc</c>/<c>cfc</c>) write <c>rt</c>. Writes to
    /// <c>$zero</c> are reported as register 0, which callers must keep at zero.
    /// </summary>
    public int? Destination => Opcode switch
    {
        0x00 => Function switch
        {
            // sll..srav, mfhi, mflo, add..sltu, jalr.
            0x00 or 0x02 or 0x03 or 0x04 or 0x06 or 0x07 or 0x09 or 0x10 or 0x12 => Rd,
            >= 0x20 and <= 0x2B => Rd,
            _ => null,
        },
        // bltzal/bgezal link through $ra.
        0x01 => (Rt & 0x10) != 0 ? 31 : null,
        0x03 => 31,
        // addi..lui.
        >= 0x08 and <= 0x0F => Rt,
        // mfc0/cfc0, mfc2/cfc2.
        0x10 or 0x12 => (Rs is 0x00 or 0x02) ? Rt : null,
        // lb..lwr.
        >= 0x20 and <= 0x26 => Rt,
        _ => null,
    };

    /// <summary>
    /// Absolute target of a <c>j</c>/<c>jal</c>: the 26-bit word index inside the 256 MiB segment
    /// containing <paramref name="segmentAddress"/> (any address of the code being decoded).
    /// </summary>
    public uint JumpTarget(uint segmentAddress)
    {
        if (Operation is not (MipsOperation.J or MipsOperation.Jal))
            throw new MipsDecodeException($"Instruction 0x{Word:X8} at offset 0x{Offset:X} is not a direct jump.");
        return (segmentAddress & 0xF0000000u) | ((Word & 0x03FFFFFFu) << 2);
    }

    /// <summary>
    /// Decodes every word of <paramref name="code"/> in the byte window [<paramref name="start"/>,
    /// <paramref name="end"/>). Both bounds must be word aligned and inside the buffer.
    /// </summary>
    public static MipsInstruction[] DecodeRange(ReadOnlySpan<byte> code, int start, int end)
    {
        if (start < 0 || end > code.Length || start > end)
            throw new MipsDecodeException($"Instruction window 0x{start:X}..0x{end:X} is outside the {code.Length}-byte buffer.");
        if (start % sizeof(uint) != 0 || end % sizeof(uint) != 0)
            throw new MipsDecodeException($"Instruction window 0x{start:X}..0x{end:X} is not word aligned.");
        var instructions = new MipsInstruction[(end - start) / sizeof(uint)];
        for (int index = 0; index < instructions.Length; index++)
        {
            int offset = start + index * sizeof(uint);
            instructions[index] = new MipsInstruction(offset, BinaryPrimitives.ReadUInt32LittleEndian(code.Slice(offset, sizeof(uint))));
        }
        return instructions;
    }

    /// <summary>
    /// End of the last whole word of a buffer. Disc overlays are code followed by data, so a
    /// trailing partial word is data and is never decoded.
    /// </summary>
    public static int WholeWordEnd(int length) => length - length % sizeof(uint);
}

/// <summary>An instruction stream or analysis result that cannot be decoded without guessing.</summary>
public sealed class MipsDecodeException(string message) : FormatException(message);
