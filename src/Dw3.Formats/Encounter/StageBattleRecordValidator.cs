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

    private static bool TryGetOffset(ReadOnlySpan<byte> overlay, uint address, uint overlayBase, out long offset)
    {
        offset = (long)address - overlayBase;
        return address >= overlayBase && offset < overlay.Length;
    }
}
