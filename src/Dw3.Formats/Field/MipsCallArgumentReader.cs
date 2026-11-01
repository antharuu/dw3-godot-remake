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

    private static bool IsControlFlow(int opcode, uint instruction) =>
        opcode is 1 or 2 or 4 or 5 or 6 or 7 or 20 or 21 or 22 or 23 or 29 ||
        (opcode == 0 && (instruction & 0x3F) is 8 or 9);

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
