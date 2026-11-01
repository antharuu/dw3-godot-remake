using System.Buffers.Binary;

namespace Dw3.Formats.Encounter;

/// <summary>
/// Validates FieldBattles records and their referenced BattleList records.
/// </summary>
internal static class StageBattleRecordValidator
{
    /// <summary>
    /// Counts consecutive valid FieldBattles entries beginning at an overlay address.
    /// </summary>
    /// <param name="overlay">Raw overlay bytes.</param>
    /// <param name="arrayAddress">Array address in overlay RAM.</param>
    /// <param name="overlayBase">Overlay load address.</param>
    /// <param name="maximumEntries">Maximum entries to inspect.</param>
    /// <returns>Number of valid records.</returns>
    internal static int CountValidEntries(ReadOnlySpan<byte> overlay, uint arrayAddress, uint overlayBase, int maximumEntries)
    {
        if (!TryGetOffset(overlay, arrayAddress, overlayBase, out long baseOffset))
        {
            return 0;
        }

        int count = 0;
        for (int index = 0; index < maximumEntries; index++)
        {
            long entryOffset = baseOffset + ((long)index * FieldEncounterDecoder.FieldBattlesStride);
            if (entryOffset + FieldEncounterDecoder.FieldBattlesStride > overlay.Length || !IsValidFieldBattles(overlay, (int)entryOffset, overlayBase))
            {
                break;
            }

            count++;
        }

        return count;
    }

    /// <summary>
    /// Checks the fixed field-battle record padding and its four battle-list pointers.
    /// </summary>
    /// <param name="overlay">Raw stage overlay bytes.</param>
    /// <param name="offset">Byte offset of the field-battle record in <paramref name="overlay"/>.</param>
    /// <param name="overlayBase">RAM address corresponding to byte zero of the overlay.</param>
    /// <returns><see langword="true"/> when the record and all referenced lists are bounded.</returns>
    private static bool IsValidFieldBattles(ReadOnlySpan<byte> overlay, int offset, uint overlayBase)
    {
        if (offset + FieldEncounterDecoder.FieldBattlesStride > overlay.Length || BinaryPrimitives.ReadInt32LittleEndian(overlay.Slice(offset + 8, 4)) != 0)
        {
            return false;
        }

        for (int index = 0; index < 4; index++)
        {
            uint pointer = BinaryPrimitives.ReadUInt32LittleEndian(overlay.Slice(offset + 12 + (index * 4), 4));
            if (!IsValidBattleList(overlay, pointer, overlayBase))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Checks one RAM pointer to a fixed-size battle-list record and its area pointers.
    /// </summary>
    /// <param name="overlay">Raw stage overlay bytes.</param>
    /// <param name="pointer">RAM address stored in a field-battle record.</param>
    /// <param name="overlayBase">RAM address corresponding to byte zero of the overlay.</param>
    /// <returns><see langword="true"/> when the list is completely contained in the overlay.</returns>
    private static bool IsValidBattleList(ReadOnlySpan<byte> overlay, uint pointer, uint overlayBase)
    {
        if (!TryGetOffset(overlay, pointer, overlayBase, out long offset) || offset + FieldEncounterDecoder.BattleListStride > overlay.Length)
        {
            return false;
        }

        int count = BinaryPrimitives.ReadInt32LittleEndian(overlay.Slice((int)offset, 4));
        if (count < 0 || count >= FieldEncounterDecoder.BattleRatesCount)
        {
            return false;
        }

        return HasValidAreaBattlePointers(overlay, (int)offset, overlayBase);
    }

    /// <summary>
    /// Verifies the eight area-battle pointers stored after a battle-list count word.
    /// </summary>
    /// <param name="overlay">Raw stage overlay bytes.</param>
    /// <param name="offset">Byte offset of the battle-list count word.</param>
    /// <param name="overlayBase">RAM address corresponding to byte zero of the overlay.</param>
    /// <returns><see langword="true"/> when every pointer addresses a full area-battle record.</returns>
    private static bool HasValidAreaBattlePointers(ReadOnlySpan<byte> overlay, int offset, uint overlayBase)
    {
        for (int index = 0; index < 8; index++)
        {
            uint pointer = BinaryPrimitives.ReadUInt32LittleEndian(overlay.Slice(offset + 4 + (index * 4), 4));
            if (!TryGetOffset(overlay, pointer, overlayBase, out long areaOffset) || areaOffset + FieldEncounterDecoder.AreaBattleStride > overlay.Length)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Converts an overlay RAM address to a bounded signed byte offset.
    /// </summary>
    /// <param name="overlay">Raw overlay whose length bounds the resulting offset.</param>
    /// <param name="address">RAM address to translate.</param>
    /// <param name="overlayBase">RAM address corresponding to byte zero of the overlay.</param>
    /// <param name="offset">Translated byte offset when the address is in range.</param>
    /// <returns><see langword="true"/> when <paramref name="address"/> lies before the overlay end.</returns>
    private static bool TryGetOffset(ReadOnlySpan<byte> overlay, uint address, uint overlayBase, out long offset)
    {
        offset = (long)address - overlayBase;
        return address >= overlayBase && offset < overlay.Length;
    }
}
