using System.Buffers.Binary;

namespace Dw3.Formats.Field;

/// <summary>
/// Recovers two members of the field stage descriptor that a WSTAG overlay fills in
/// during its setup routine: the map-object table pointer and the sprite-sheet entry.
///
/// Disc layout: the executable keeps one field stage descriptor at RAM address
/// <see cref="DescriptorAddress"/>. Its 32-bit member at +0x0C holds the stage's sprite
/// sheet as <c>(TMPK file number &lt;&lt; 16) | archive entry</c>, and the member at +0x10
/// points at the overlay's StageTile table. The overlay writes both with plain
/// <c>sw</c> instructions whose base register and value are built from
/// <c>lui</c>/<c>addiu</c>/<c>ori</c> constants.
///
/// The reader is a bounded forward constant propagation, not an emulator: values only
/// survive straight-line code. A <c>jal</c>/<c>jalr</c> clobbers the caller-saved
/// registers after its delay slot and any other jump or branch forgets every register.
/// A store whose value is unknown, or two stores with different values, is rejected.
/// </summary>
public static class StageDescriptorReader
{
    /// <summary>RAM address of the field stage descriptor in the supported executable.</summary>
    public const uint DescriptorAddress = 0x80099D80;

    // Member offsets inside the descriptor.
    private const uint SheetEntryMember = 0x0C;
    private const uint ObjectsMember = 0x10;

    // MIPS caller-saved registers: at, v0-v1, a0-a3, t0-t9, ra.
    private static readonly int[] CallerSavedRegisters = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 24, 25, 31];

    /// <summary>Reads the object-table address and sprite-sheet entry from raw overlay bytes.</summary>
    /// <exception cref="FormatException">The overlay does not assign both members exactly once in constant form.</exception>
    public static StageDescriptor Read(ReadOnlySpan<byte> overlay)
    {
        if (overlay.Length == 0 || overlay.Length % sizeof(uint) != 0)
            throw new FormatException("MIPS overlay instruction stream is truncated.");

        var stores = new Dictionary<uint, HashSet<uint?>> { [SheetEntryMember] = [], [ObjectsMember] = [] };
        uint?[] registers = new uint?[32];
        registers[0] = 0;
        PendingJump pending = PendingJump.None;
        for (int offset = 0; offset < overlay.Length; offset += sizeof(uint))
        {
            uint instruction = BinaryPrimitives.ReadUInt32LittleEndian(overlay.Slice(offset, sizeof(uint)));
            Execute(instruction, registers, stores);
            registers[0] = 0;

            // The previous instruction was a jump: its delay slot has just executed.
            if (pending == PendingJump.Call)
                foreach (int register in CallerSavedRegisters)
                    registers[register] = null;
            else if (pending == PendingJump.Other)
                Array.Fill(registers, null, 1, 31);
            pending = Classify(instruction);
        }

        uint objects = Single(stores[ObjectsMember], "object table");
        uint sheet = Single(stores[SheetEntryMember], "sprite sheet");
        if (objects < FieldOverlayLayout.LoadAddress || objects >= FieldOverlayLayout.LoadAddress + (uint)overlay.Length)
            throw new FormatException($"Stage descriptor object table 0x{objects:X8} lies outside the overlay image.");
        return new StageDescriptor(objects, (int)(sheet & 0xFFFF));
    }

    private static void Execute(uint instruction, uint?[] registers, Dictionary<uint, HashSet<uint?>> stores)
    {
        int opcode = (int)(instruction >> 26);
        int source = (int)((instruction >> 21) & 31);
        int target = (int)((instruction >> 16) & 31);
        ushort immediate = (ushort)instruction;
        switch (opcode)
        {
            case 15: // lui
                registers[target] = (uint)immediate << 16;
                break;
            case 9: // addiu
                registers[target] = registers[source] is uint value ? unchecked(value + (uint)(short)immediate) : null;
                break;
            case 13: // ori
                registers[target] = registers[source] is uint bits ? bits | immediate : null;
                break;
            case 43: // sw
                if (registers[source] is uint baseAddress)
                {
                    uint member = unchecked(baseAddress + (uint)(short)immediate - DescriptorAddress);
                    if (stores.TryGetValue(member, out HashSet<uint?>? values))
                        values.Add(registers[target]);
                }
                break;
            case 0:
                InvalidateSpecialDestination(instruction, registers);
                break;
            case 3: // jal writes ra
                registers[31] = null;
                break;
            default:
                // Remaining immediate ALU ops and loads write rt; stores and branches do not.
                if (opcode is >= 8 and <= 14 || opcode is >= 32 and <= 38)
                    registers[target] = null;
                break;
        }
    }

    private static void InvalidateSpecialDestination(uint instruction, uint?[] registers)
    {
        int function = (int)(instruction & 0x3F);
        // Shifts, ALU ops, mfhi/mflo and jalr write rd.
        if (function is 0 or 2 or 3 or 4 or 6 or 7 or 9 or 16 or 18 || function is >= 32 and <= 43)
            registers[(instruction >> 11) & 31] = null;
    }

    private static PendingJump Classify(uint instruction)
    {
        int opcode = (int)(instruction >> 26);
        int function = (int)(instruction & 0x3F);
        if (opcode == 3 || (opcode == 0 && function == 9))
            return PendingJump.Call;
        if (opcode is 1 or 2 or 4 or 5 or 6 or 7 or 20 or 21 or 22 or 23 || (opcode == 0 && function == 8))
            return PendingJump.Other;
        return PendingJump.None;
    }

    private static uint Single(HashSet<uint?> values, string member)
    {
        if (values.Count == 0)
            throw new FormatException($"MIPS overlay never assigns the stage descriptor {member} member.");
        if (values.Contains(null))
            throw new FormatException($"MIPS overlay stores a value that is not a constant into the stage descriptor {member} member.");
        if (values.Count > 1)
            throw new FormatException($"MIPS overlay assigns conflicting values to the stage descriptor {member} member.");
        return values.Single()!.Value;
    }

    private enum PendingJump
    {
        None,
        Call,
        Other,
    }
}

/// <summary>Stage descriptor members recovered from a WSTAG overlay.</summary>
/// <param name="ObjectsAddress">RAM address of the overlay's StageTile table.</param>
/// <param name="SheetEntry">Archive entry of the stage sprite sheet inside the stage TMPK file.</param>
public sealed record StageDescriptor(uint ObjectsAddress, int SheetEntry);
