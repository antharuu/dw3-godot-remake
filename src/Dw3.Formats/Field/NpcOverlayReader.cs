using System.Buffers.Binary;

namespace Dw3.Formats.Field;

/// <summary>
/// Reads NPC-related data structures from a field stage overlay (.PRO file).
///
/// Disc layout constants:
///   STAGE_VRAM = 0x800A5DE0  (stage overlays load at this RAM address, EU)
///   CODES_END  = 0xFFFF      (condition list terminator)
///
/// FieldActorEntry layout (little-endian, 18 bytes used of a 20-byte struct):
///   +0x00  u32  conditions pointer
///   +0x04  u32  talks pointer
///   +0x08  s16  actor_id
///   +0x0A  s16  image
///   +0x0C  s16  x
///   +0x0E  s16  y
///   +0x10  s16  direction (valid range 0..7)
///
/// Condition list (u16 pairs until 0xFFFF):
///   [code u16][value u16] ... [0xFFFF]
///
/// StageSlot layout (little-endian, 24 bytes):
///   +0x00  u16[4]  condition pair× 2 (ignored in this decoder)
///   +0x08  u16     type (0 = list terminator)
///   +0x0A  u16     arg
///   +0x0C  u16     x
///   +0x0E  u16     y
///   +0x10  u16     direction
///   +0x12  u16     hide_anim (ignored)
///   +0x14  u16     place (ignored)
///   +0x16  u16     place_arg (ignored).
/// </summary>
public static class NpcOverlayReader
{
    private const int ActorEntryBytes = 18;
    private const ushort ConditionsEnd = 0xFFFF;
    private const int MaxConditions = 512;
    private const int StageSlotBytes = 24;
    private const int MaxSlots = 64;

    /// <summary>
    /// Reads a FieldActorEntry at the given overlay RAM address.
    /// </summary>
    /// <param name="overlay">Raw overlay bytes (the .PRO file contents).</param>
    /// <param name="actorAddress">RAM address of the entry (must be >= overlayLoadBase).</param>
    /// <param name="overlayLoadBase">RAM address at which the overlay is loaded.</param>
    /// <returns>The decoded actor entry with actor id, coordinates and visibility state.</returns>
    /// <exception cref="FormatException">The address falls below the load base or overruns the overlay.</exception>
    public static NpcActorEntry ReadActorEntry(ReadOnlySpan<byte> overlay, uint actorAddress, uint overlayLoadBase)
    {
        if (actorAddress < overlayLoadBase)
        {
            throw new FormatException(
                $"Actor address 0x{actorAddress:X8} is below overlay load base 0x{overlayLoadBase:X8}.");
        }

        int fileOffset = checked((int)(actorAddress - overlayLoadBase));
        if (fileOffset + ActorEntryBytes > overlay.Length)
        {
            throw new FormatException(
                $"Actor record at overlay offset 0x{fileOffset:X} extends beyond overlay ({overlay.Length} bytes).");
        }

        uint conditionsPtr = BinaryPrimitives.ReadUInt32LittleEndian(overlay.Slice(fileOffset, 4));
        uint talksPtr = BinaryPrimitives.ReadUInt32LittleEndian(overlay.Slice(fileOffset + 4, 4));
        short actorId = BinaryPrimitives.ReadInt16LittleEndian(overlay.Slice(fileOffset + 8, 2));
        short image = BinaryPrimitives.ReadInt16LittleEndian(overlay.Slice(fileOffset + 10, 2));
        short x = BinaryPrimitives.ReadInt16LittleEndian(overlay.Slice(fileOffset + 12, 2));
        short y = BinaryPrimitives.ReadInt16LittleEndian(overlay.Slice(fileOffset + 14, 2));
        short direction = BinaryPrimitives.ReadInt16LittleEndian(overlay.Slice(fileOffset + 16, 2));

        if (direction is < 0 or > 7)
        {
            throw new FormatException($"Actor at 0x{actorAddress:X8} has invalid direction {direction} (expected 0..7).");
        }

        return new NpcActorEntry(conditionsPtr, talksPtr, actorId, image, x, y, direction);
    }

    /// <summary>
    /// Reads the (code, value) condition list at the given pointer.
    /// Returns an empty list when <paramref name="conditionsPtr"/> is 0 (no conditions).
    /// </summary>
    /// <param name="overlay">Raw overlay bytes.</param>
    /// <param name="conditionsPtr">RAM pointer to the condition list (0 = unconditional).</param>
    /// <param name="overlayLoadBase">RAM address at which the overlay is loaded.</param>
    /// <returns>The decoded condition list; empty when <paramref name="conditionsPtr"/> is 0.</returns>
    /// <exception cref="FormatException">The pointer falls below the load base, overruns the overlay, or a condition id is invalid.</exception>
    public static IReadOnlyList<NpcCondition> ReadConditionList(
        ReadOnlySpan<byte> overlay, uint conditionsPtr, uint overlayLoadBase)
    {
        if (conditionsPtr == 0u)
        {
            return [];
        }

        if (conditionsPtr < overlayLoadBase)
        {
            throw new FormatException(
                $"Condition pointer 0x{conditionsPtr:X8} is below overlay load base 0x{overlayLoadBase:X8}.");
        }

        int fileOffset = checked((int)(conditionsPtr - overlayLoadBase));

        var result = new List<NpcCondition>();
        int cursor = fileOffset;
        while (true)
        {
            if (cursor + 2 > overlay.Length)
            {
                throw new FormatException(
                    $"Condition list at 0x{conditionsPtr:X8} reads past end of overlay.");
            }

            ushort code = BinaryPrimitives.ReadUInt16LittleEndian(overlay.Slice(cursor, 2));
            if (code == ConditionsEnd)
            {
                break;
            }

            if (cursor + 4 > overlay.Length)
            {
                throw new FormatException(
                    $"Condition value at 0x{conditionsPtr + (uint)(cursor - fileOffset):X8} reads past end of overlay.");
            }

            ushort value = BinaryPrimitives.ReadUInt16LittleEndian(overlay.Slice(cursor + 2, 2));
            result.Add(new NpcCondition(code, value));
            cursor += 4;
            if (result.Count > MaxConditions)
            {
                throw new FormatException(
                    $"Condition list at 0x{conditionsPtr:X8} exceeded {MaxConditions} entries without terminator.");
            }
        }

        return result;
    }

    /// <summary>
    /// Reads the StageSlot table at the given address.
    /// Returns an empty list when the first record is a terminator (type 0).
    /// </summary>
    /// <param name="overlay">Raw overlay bytes.</param>
    /// <param name="slotsAddress">RAM address of the first StageSlot record.</param>
    /// <param name="overlayLoadBase">RAM address at which the overlay is loaded.</param>
    /// <returns>The decoded stage slots; empty when the first record is a zero-type terminator.</returns>
    /// <exception cref="FormatException">The address falls below the load base or overruns the overlay.</exception>
    public static IReadOnlyList<NpcStageSlot> ReadStageSlots(
        ReadOnlySpan<byte> overlay, uint slotsAddress, uint overlayLoadBase)
    {
        if (slotsAddress < overlayLoadBase)
        {
            throw new FormatException(
                $"Stage slots address 0x{slotsAddress:X8} is below overlay load base 0x{overlayLoadBase:X8}.");
        }

        int fileOffset = checked((int)(slotsAddress - overlayLoadBase));

        var result = new List<NpcStageSlot>();
        int cursor = fileOffset;
        while (true)
        {
            if (cursor + StageSlotBytes > overlay.Length)
            {
                throw new FormatException(
                    $"Stage slot {result.Count} at offset 0x{cursor:X} extends past end of overlay.");
            }

            // type is at +0x08 in the 24-byte record
            ushort type = BinaryPrimitives.ReadUInt16LittleEndian(overlay.Slice(cursor + 8, 2));
            if (type == 0)
            {
                break;
            }

            ushort arg = BinaryPrimitives.ReadUInt16LittleEndian(overlay.Slice(cursor + 10, 2));
            ushort x = BinaryPrimitives.ReadUInt16LittleEndian(overlay.Slice(cursor + 12, 2));
            ushort y = BinaryPrimitives.ReadUInt16LittleEndian(overlay.Slice(cursor + 14, 2));
            ushort dir = BinaryPrimitives.ReadUInt16LittleEndian(overlay.Slice(cursor + 16, 2));
            result.Add(new NpcStageSlot(type, arg, x, y, dir));
            cursor += StageSlotBytes;
            if (result.Count >= MaxSlots)
            {
                throw new FormatException(
                    $"Stage slot table at 0x{slotsAddress:X8} exceeded {MaxSlots} entries without terminator.");
            }
        }

        return result;
    }
}
