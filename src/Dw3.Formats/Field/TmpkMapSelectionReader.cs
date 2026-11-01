using System.Buffers.Binary;

namespace Dw3.Formats.Field;

/// <summary>
/// Recovers the floor and trigger TMPK entries from the bounded setup sequence in a
/// raw stage overlay. This recognizes only the observed constant setup form and
/// rejects control flow or competing sequences instead of selecting a default entry.
/// </summary>
public static class TmpkMapSelectionReader
{
    private const int MaximumSequenceWords = 96;

    /// <summary>Recovers the two required map archive entries from raw overlay bytes.</summary>
    public static TmpkMapSelection Recover(ReadOnlySpan<byte> overlay)
    {
        if (overlay.Length == 0 || overlay.Length % sizeof(uint) != 0)
            throw new FormatException("MIPS overlay instruction stream is truncated.");

        uint[] words = new uint[overlay.Length / sizeof(uint)];
        for (int index = 0; index < words.Length; index++)
            words[index] = BinaryPrimitives.ReadUInt32LittleEndian(overlay.Slice(index * sizeof(uint), sizeof(uint)));

        TmpkMapSelection? recovered = null;
        for (int start = 0; start < words.Length; start++)
        {
            TmpkMapSelection? candidate = null;
            if (IsZeroA0(words[start]) &&
                TryReadFileIdentifier(words, start + 1, out int firstEnd, out ushort archive, out int floor))
            {
                int? trigger = TryFindTriggerSequence(words, firstEnd + 1, archive, out int recoveredTrigger)
                    ? recoveredTrigger
                    : null;
                candidate = new TmpkMapSelection(floor, trigger, null);
            }
            else if (TryReadOneBasedA0Loader(words, start, out TmpkMapSelection oneBased))
                candidate = oneBased;
            else if (TryReadOneBasedA1Descriptor(words, start, out TmpkMapSelection descriptor))
                candidate = descriptor;

            if (candidate is null)
                continue;
            if (recovered is not null && recovered != candidate)
                throw new FormatException("MIPS overlay has multiple conflicting TMPK setup sequences.");
            recovered = candidate;
        }
        return recovered ?? throw new FormatException("MIPS overlay has no supported TMPK setup sequence.");
    }

    private static bool TryReadOneBasedA1Descriptor(uint[] words, int start, out TmpkMapSelection selection)
    {
        selection = null!;
        if (start + 1 >= words.Length ||
            !IsLui(words[start], 5, out ushort archiveUpper) || archiveUpper < 0x0100 || archiveUpper >= 0x8000 ||
            !IsOri(words[start + 1], 5, 5, out ushort oneBasedEntry) || oneBasedEntry == 0)
            return false;

        int limit = Math.Min(words.Length, start + MaximumSequenceWords);
        bool initializedDescriptor = false;
        bool storedFileIdentifier = false;
        for (int index = start + 2; index < limit; index++)
        {
            if (IsControlFlow(words[index]))
            {
                if (storedFileIdentifier && IsJalr(words[index], 2))
                {
                    selection = new TmpkMapSelection(oneBasedEntry - 1, null, null);
                    return true;
                }
                return false;
            }
            if (!initializedDescriptor && IsLui(words[index], 3, out ushort descriptorUpper) && descriptorUpper >= 0x8000 &&
                index + 1 < limit && IsAddiuToRegister(words[index + 1], 3, 3))
            {
                initializedDescriptor = true;
                continue;
            }
            if (initializedDescriptor && IsStoreWord(words[index], 5, 3, 12))
            {
                storedFileIdentifier = true;
                continue;
            }
        }
        return false;
    }

    private static bool TryReadOneBasedA0Loader(uint[] words, int start, out TmpkMapSelection selection)
    {
        selection = null!;
        if (!IsLui(words[start], 4, out ushort archiveUpper) || archiveUpper < 0x0100 || archiveUpper >= 0x8000 || start + 5 >= words.Length ||
            !IsLui(words[start + 1], 2, out ushort loaderUpper) || loaderUpper < 0x8000 ||
            !IsLoadWord(words[start + 2], 2, 2, 0x4F5C) ||
            words[start + 3] != 0 ||
            !IsJalr(words[start + 4], 2) ||
            !IsOri(words[start + 5], 4, 4, out ushort oneBasedEntry) ||
            oneBasedEntry == 0)
            return false;

        selection = new TmpkMapSelection(oneBasedEntry - 1, null, null);
        return true;
    }

    private static bool TryFindTriggerSequence(uint[] words, int start, ushort archive, out int trigger)
    {
        int limit = Math.Min(words.Length, start + MaximumSequenceWords);
        for (int index = start; index < limit; index++)
        {
            if (!IsAddiu(words[index], 4, 0, 7))
                continue;
            if (index + 2 < words.Length && IsControlFlow(words[index + 1]))
                RejectControlFlow(words[index + 1], index + 1);
            if (index + 1 >= words.Length || !IsLui(words[index + 1], 5, out ushort candidateArchive))
                continue;
            if (!TryReadEntryAtCall(words, index + 2, limit, out trigger))
                continue;
            if (candidateArchive != archive)
                continue;
            return true;
        }
        trigger = 0;
        return false;
    }

    private static bool TryReadEntryAtCall(uint[] words, int start, int limit, out int entry)
    {
        for (int index = start; index < limit; index++)
        {
            if (IsAddiuToRegister(words[index], 4, 0))
                break;
            RejectControlFlow(words[index], index, allowJalr: true);
            if (IsOri(words[index], 5, 5, out ushort direct))
            {
                entry = direct;
                return HasJalr(words, index + 1, limit);
            }
            if ((words[index] >> 26) == 0 && (words[index] & 0x3F) == 9 && index + 1 < words.Length && IsOri(words[index + 1], 5, 5, out ushort delayed))
            {
                entry = delayed;
                return true;
            }
        }
        entry = 0;
        return false;
    }

    private static bool TryReadFileIdentifier(uint[] words, int start, out int end, out ushort archive, out int entry)
    {
        end = start;
        archive = 0;
        entry = 0;
        if (start >= words.Length || !IsLui(words[start], 5, out archive))
            return false;
        if (TryReadEntryAtCall(words, start + 1, Math.Min(words.Length, start + MaximumSequenceWords), out entry))
        {
            end = start;
            return true;
        }
        end = start;
        entry = 0;
        return true;
    }

    private static bool HasJalr(uint[] words, int start, int limit)
    {
        for (int index = start; index < limit; index++)
        {
            RejectControlFlow(words[index], index, allowJalr: true);
            if ((words[index] >> 26) == 0 && (words[index] & 0x3F) == 9)
                return true;
        }
        return false;
    }

    private static bool IsZeroA0(uint word) => IsAddiu(word, 4, 0, 0) ||
        (word >> 26) == 0 && ((word >> 21) & 31) == 0 && ((word >> 11) & 31) == 4 && (word & 0x3F) == 0x21;

    private static bool IsAddiu(uint word, int target, int source, short immediate) =>
        (word >> 26) == 9 && ((word >> 16) & 31) == target && ((word >> 21) & 31) == source && (short)word == immediate;
    private static bool IsAddiuToRegister(uint word, int target, int source) =>
        (word >> 26) == 9 && ((word >> 16) & 31) == target && ((word >> 21) & 31) == source;
    private static bool IsLui(uint word, int target, out ushort immediate)
    {
        immediate = (ushort)word;
        return (word >> 26) == 15 && ((word >> 16) & 31) == target;
    }
    private static bool IsOri(uint word, int target, int source, out ushort immediate)
    {
        immediate = (ushort)word;
        return (word >> 26) == 13 && ((word >> 16) & 31) == target && ((word >> 21) & 31) == source;
    }

    private static bool IsJalr(uint word, int source) =>
        (word >> 26) == 0 && ((word >> 21) & 31) == source && (word & 0x3F) == 9;
    private static bool IsLoadWord(uint word, int target, int source, ushort displacement) =>
        (word >> 26) == 35 && ((word >> 16) & 31) == target && ((word >> 21) & 31) == source && (ushort)word == displacement;
    private static bool IsStoreWord(uint word, int source, int target, ushort displacement) =>
        (word >> 26) == 43 && ((word >> 16) & 31) == source && ((word >> 21) & 31) == target && (ushort)word == displacement;

    private static bool IsControlFlow(uint word)
    {
        int opcode = (int)(word >> 26);
        return opcode is 1 or 2 or 3 or 4 or 5 or 6 or 7 or 20 or 21 or 22 or 23 or 29 ||
            (opcode == 0 && (word & 0x3F) is 8 or 9);
    }

    private static void RejectControlFlow(uint word, int index, bool allowJalr = false)
    {
        int opcode = (int)(word >> 26);
        bool jalr = opcode == 0 && (word & 0x3F) == 9;
        if (opcode is 1 or 2 or 3 or 4 or 5 or 6 or 7 or 20 or 21 or 22 or 23 or 29 || (!allowJalr && jalr) || (opcode == 0 && (word & 0x3F) == 8))
            throw new FormatException($"Unsupported MIPS control flow at overlay word {index}.");
    }
}

/// <summary>Disc-proven TMPK archive entries selected by a stage overlay.</summary>
public sealed record TmpkMapSelection(int FloorEntry, int? TriggerEntry, int? AreasEntry);
