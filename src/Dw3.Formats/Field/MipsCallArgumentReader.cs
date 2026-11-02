using System.Buffers.Binary;

namespace Dw3.Formats.Field;

/// <summary>
/// Reads constant arguments at a direct MIPS call in a raw stage overlay.
/// This is deliberately not a general MIPS emulator: branches, memory loads,
/// indirect calls, and arithmetic data flow are rejected rather than inferred.
/// </summary>
public static class MipsCallArgumentReader
{
    /// <summary>
    /// Finds exactly one direct <c>jal</c> to <paramref name="targetWordAddress"/>
    /// and recovers only arguments established by supported constant instructions.
    /// </summary>
    /// <param name="overlay">MIPS overlay bytes; must be a whole-word instruction stream.</param>
    /// <param name="targetWordAddress">Word address (PC / 4) of the called function.</param>
    /// <returns>The recovered call arguments; undetermined registers have null values.</returns>
    /// <exception cref="FormatException">The overlay is not a word-aligned MIPS stream, or it does not contain exactly one matching call.</exception>
    public static MipsCallArguments RecoverDirectCall(ReadOnlySpan<byte> overlay, int targetWordAddress)
    {
        if (overlay.Length == 0 || overlay.Length % sizeof(uint) != 0)
        {
            throw new FormatException("MIPS overlay instruction stream is truncated.");
        }

        if ((uint)targetWordAddress > 0x03FFFFFFu)
        {
            throw new ArgumentOutOfRangeException(nameof(targetWordAddress));
        }

        uint?[] registers = new uint?[32];
        registers[0] = 0;
        MipsCallArguments? recovered = null;
        for (int offset = 0; offset < overlay.Length; offset += sizeof(uint))
        {
            uint instruction = BinaryPrimitives.ReadUInt32LittleEndian(overlay.Slice(offset, sizeof(uint)));
            int opcode = (int)(instruction >> 26);
            int source = (int)((instruction >> 21) & 31);
            int target = (int)((instruction >> 16) & 31);
            ushort immediate = (ushort)instruction;

            if (opcode == 3)
            {
                recovered = MatchDirectCall(instruction, targetWordAddress, recovered, registers);
                continue;
            }

            if (IsControlFlow(opcode, instruction))
            {
                throw new FormatException($"Unsupported MIPS control flow at overlay offset 0x{offset:X}.");
            }

            ApplyInstruction(opcode, source, target, immediate, instruction, registers);
            registers[0] = 0;
        }

        return recovered ?? throw new FormatException("MIPS overlay does not contain the requested direct call.");
    }

    /// <summary>
    /// Tests whether <paramref name="instruction"/> is a <c>jal</c> to <paramref name="targetWordAddress"/>.
    /// If so, builds and returns the recovered arguments; if another call was already found, throws.
    /// </summary>
    /// <param name="instruction">Raw 32-bit MIPS instruction word.</param>
    /// <param name="targetWordAddress">Word address of the target function.</param>
    /// <param name="recovered">Previous recovered arguments for this scan pass, or null if none yet.</param>
    /// <param name="registers">Current register state ($4–$7 are the argument registers of interest).</param>
    /// <returns>The newly built arguments when this instruction is a matching <c>jal</c>; otherwise <paramref name="recovered"/>.</returns>
    /// <exception cref="FormatException">A second matching call appears in the same overlay.</exception>
    private static MipsCallArguments? MatchDirectCall(uint instruction, int targetWordAddress, MipsCallArguments? recovered, uint?[] registers)
    {
        int destination = (int)(instruction & 0x03FFFFFF);
        if (destination != targetWordAddress)
        {
            return recovered;
        }

        if (recovered is not null)
        {
            throw new FormatException("MIPS overlay has multiple matching direct calls.");
        }

        return new MipsCallArguments(registers[4], registers[5], registers[6], registers[7]);
    }

    /// <summary>
    /// Applies a single MIPS I-type instruction's effect on the tracked register state.
    /// Supported: <c>addiu</c> (op 9), <c>ori</c> (op 13), <c>lui</c> (op 15). Unsupported instructions
    /// call <see cref="InvalidateDestination"/> instead.
    /// </summary>
    /// <param name="opcode">6-bit opcode field of the instruction.</param>
    /// <param name="source">5-bit rs field of the instruction.</param>
    /// <param name="target">5-bit rt field (destination register).</param>
    /// <param name="immediate">16-bit immediate field of the instruction.</param>
    /// <param name="instruction">Full 32-bit instruction word, forwarded to <see cref="InvalidateDestination"/>.</param>
    /// <param name="registers">Register state array; modified in-place.</param>
    private static void ApplyInstruction(int opcode, int source, int target, ushort immediate, uint instruction, uint?[] registers)
    {
        switch (opcode)
        {
            case 9: // addiu rt, rs, immediate
                registers[target] = registers[source] is uint value
                    ? unchecked(value + (uint)(short)immediate)
                    : null;
                break;
            case 13: // ori rt, rs, immediate
                registers[target] = registers[source] is uint sourceValue
                    ? sourceValue | immediate
                    : null;
                break;
            case 15: // lui rt, immediate
                registers[target] = (uint)immediate << 16;
                break;
            default:
                InvalidateDestination(registers, opcode, target, instruction);
                break;
        }
    }

    /// <summary>
    /// Returns whether the instruction is a control-flow instruction (branch, jump, return)
    /// that should terminate constant-tracking because control paths diverge.
    /// </summary>
    /// <param name="opcode">6-bit opcode field.</param>
    /// <param name="instruction">Full 32-bit instruction word; used to check the function field for R-type instructions.</param>
    /// <returns><see langword="true"/> for branch, jump and indirect-jump instructions.</returns>
    private static bool IsControlFlow(int opcode, uint instruction) =>
        opcode is 1 or 2 or 4 or 5 or 6 or 7 or 20 or 21 or 22 or 23 or 29 ||
        (opcode == 0 && (instruction & 0x3F) is 8 or 9);

    /// <summary>
    /// Clears the destination register when an instruction has an unknown or untrackable effect
    /// on it. R-type ALU instructions (opcode 0) clear the <c>rd</c> field; I-type instructions
    /// clear <c>rt</c>.
    /// </summary>
    /// <param name="registers">Register state array; the destination entry is set to null.</param>
    /// <param name="opcode">6-bit opcode field of the instruction.</param>
    /// <param name="target">5-bit rt field of the instruction.</param>
    /// <param name="instruction">Full 32-bit instruction word; used to extract <c>rd</c> for R-type instructions.</param>
    private static void InvalidateDestination(uint?[] registers, int opcode, int target, uint instruction)
    {
        if (opcode == 0)
        {
            int function = (int)(instruction & 0x3F);
            if (function is 32 or 33 or 34 or 35 or 36 or 37 or 38 or 39 or 42 or 43)
            {
                registers[(instruction >> 11) & 31] = null;
            }

            return;
        }

        if (opcode is >= 8 and <= 27 && opcode is not 15)
        {
            registers[target] = null;
        }

        if (opcode is >= 32 and <= 55)
        {
            registers[target] = null;
        }
    }
}
