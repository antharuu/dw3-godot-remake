using System.Buffers.Binary;

namespace Dw3.Formats.Field;

/// <summary>
/// Locates a StageSlot table only when raw overlay instructions materialize an
/// absolute pointer into that same overlay. This deliberately accepts the
/// bounded MIPS <c>lui</c> + <c>addiu</c>/<c>ori</c> form and rejects ambiguous
/// candidates instead of scanning arbitrary data for slot-shaped bytes.
/// </summary>
public static class StageSlotLocator
{
    // The EU WSTAG overlay image starts at this runtime address. This is an
    // executable-image coordinate, not a stage registry or content table.
    private const uint OverlayLoadAddress = 0x800A5DE0;

    /// <summary>Locates and decodes the unique referenced slot table.</summary>
    /// <exception cref="FormatException">Thrown when more than one stageSlots assignment is present.</exception>
    public static IReadOnlyList<StageSlot> LocateAndDecode(ReadOnlySpan<byte> overlay) =>
        TryLocate(overlay)?.Slots ?? throw new FormatException("MIPS overlay has no supported StageSlot table pointer.");

    /// <summary>Locates the optional stageSlots table, returning an empty list when this overlay does not assign it.</summary>
    public static IReadOnlyList<StageSlot> TryLocateAndDecode(ReadOnlySpan<byte> overlay) =>
        TryLocate(overlay)?.Slots ?? [];

    /// <summary>Returns the raw table coordinate with the decoded slots, when the optional member is assigned.</summary>
    public static StageSlotTable? TryLocate(ReadOnlySpan<byte> overlay)
    {
        if (overlay.Length == 0 || overlay.Length % sizeof(uint) != 0)
        {
            throw new FormatException("MIPS overlay instruction stream is truncated.");
        }

        var candidates = new Dictionary<int, IReadOnlyList<StageSlot>>();
        for (int offset = 0; offset + 12 <= overlay.Length; offset += sizeof(uint))
        {
            uint first = BinaryPrimitives.ReadUInt32LittleEndian(overlay.Slice(offset, sizeof(uint)));
            uint second = BinaryPrimitives.ReadUInt32LittleEndian(overlay.Slice(offset + sizeof(uint), sizeof(uint)));
            uint third = BinaryPrimitives.ReadUInt32LittleEndian(overlay.Slice(offset + 2 * sizeof(uint), sizeof(uint)));
            if (!TryReadOverlayPointer(first, second, third, overlay.Length, out int tableOffset))
            {
                continue;
            }

            try
            {
                candidates.TryAdd(tableOffset, StageSlotDecoder.Decode(overlay, tableOffset));
            }
            catch (FormatException)
            {
                // A referenced pointer is slot evidence only if the strict decoder
                // recognizes a complete table at that address.
            }
        }

        return candidates.Count switch
        {
            0 => null,
            1 => new StageSlotTable(candidates.Single().Key, candidates.Single().Value),
            _ => throw new FormatException($"MIPS overlay has multiple conflicting StageSlot table pointers: {string.Join(", ", candidates.Keys.Select(offset => $"0x{offset:X}"))}."),
        };
    }

    private static bool TryReadOverlayPointer(uint first, uint second, uint third, int overlayLength, out int tableOffset)
    {
        tableOffset = 0;
        if ((first >> 26) != 15)
        {
            return false;
        }

        int register = (int)((first >> 16) & 31);
        int opcode = (int)(second >> 26);
        if (opcode is not 9 and not 13 || ((second >> 21) & 31) != register || ((second >> 16) & 31) != register)
        {
            return false;
        }

        // The field stage descriptor stores stageSlots at member offset 0x14.
        // Other overlay pointers can happen to decode as record-shaped data but
        // are not this member and must never become a guessed slot table.
        if ((third >> 26) != 43 || ((third >> 16) & 31) != register || (ushort)third != 0x14)
        {
            return false;
        }

        uint upper = (first & 0xffff) << 16;
        uint pointer = opcode == 9
            ? unchecked((uint)((int)upper + (short)second))
            : upper | (second & 0xffff);
        if (pointer < OverlayLoadAddress || pointer >= OverlayLoadAddress + (uint)overlayLength)
        {
            return false;
        }

        tableOffset = checked((int)(pointer - OverlayLoadAddress));
        return true;
    }
}
