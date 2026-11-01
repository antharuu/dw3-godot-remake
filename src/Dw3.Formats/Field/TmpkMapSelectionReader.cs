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
        {
            throw new FormatException("MIPS overlay instruction stream is truncated.");
        }

        uint[] words = new uint[overlay.Length / sizeof(uint)];
        for (int index = 0; index < words.Length; index++)
        {
            words[index] = BinaryPrimitives.ReadUInt32LittleEndian(overlay.Slice(index * sizeof(uint), sizeof(uint)));
        }

        TmpkMapSelection? recovered = null;
        for (int start = 0; start < words.Length; start++)
        {
            TmpkMapSelection? candidate = TryMatchCandidate(words, start);
            if (candidate is null)
            {
                continue;
            }

            if (recovered is not null && recovered != candidate)
            {
                throw new FormatException("MIPS overlay has multiple conflicting TMPK setup sequences.");
            }

            recovered = candidate;
        }

        return recovered ?? throw new FormatException("MIPS overlay has no supported TMPK setup sequence.");
    }

    private static TmpkMapSelection? TryMatchCandidate(uint[] words, int start)
    {
        if (TmpkMipsOps.IsZeroA0(words[start]) &&
            TryReadFileIdentifier(words, start + 1, out int firstEnd, out ushort archive, out int floor))
        {
            int? trigger = TmpkTriggerSequenceReader.TryFind(words, firstEnd + 1, archive, MaximumSequenceWords, out int recoveredTrigger)
                ? recoveredTrigger
                : null;
            return new TmpkMapSelection(floor, trigger, null);
        }

        if (TryReadOneBasedA0Loader(words, start, out TmpkMapSelection oneBased))
        {
            return oneBased;
        }

        if (TryReadOneBasedA1Descriptor(words, start, out TmpkMapSelection descriptor))
        {
            return descriptor;
        }

        return null;
    }

    private static bool TryReadOneBasedA1Descriptor(uint[] words, int start, out TmpkMapSelection selection)
    {
        selection = null!;
        if (start + 1 >= words.Length ||
            !TmpkMipsOps.IsLui(words[start], 5, out ushort archiveUpper) || archiveUpper < 0x0100 || archiveUpper >= 0x8000 ||
            !TmpkMipsOps.IsOri(words[start + 1], 5, 5, out ushort oneBasedEntry) || oneBasedEntry == 0)
        {
            return false;
        }

        int limit = Math.Min(words.Length, start + MaximumSequenceWords);
        bool initializedDescriptor = false;
        bool storedFileIdentifier = false;
        for (int index = start + 2; index < limit; index++)
        {
            if (TmpkMipsOps.IsControlFlow(words[index]))
            {
                return TryCompleteA1Descriptor(words, index, storedFileIdentifier, oneBasedEntry, out selection);
            }

            ClassifyA1Instruction(words, index, limit, ref initializedDescriptor, ref storedFileIdentifier);
        }

        return false;
    }

    private static bool TryCompleteA1Descriptor(uint[] words, int index, bool storedFileIdentifier, ushort oneBasedEntry, out TmpkMapSelection selection)
    {
        selection = null!;
        if (storedFileIdentifier && TmpkMipsOps.IsJalr(words[index], 2))
        {
            selection = new TmpkMapSelection(oneBasedEntry - 1, null, null);
            return true;
        }

        return false;
    }

    private static void ClassifyA1Instruction(uint[] words, int index, int limit, ref bool initializedDescriptor, ref bool storedFileIdentifier)
    {
        if (!initializedDescriptor && TmpkMipsOps.IsLui(words[index], 3, out ushort descriptorUpper) && descriptorUpper >= 0x8000 &&
            index + 1 < limit && TmpkMipsOps.IsAddiuToRegister(words[index + 1], 3, 3))
        {
            initializedDescriptor = true;
            return;
        }

        if (initializedDescriptor && TmpkMipsOps.IsStoreWord(words[index], 5, 3, 12))
        {
            storedFileIdentifier = true;
        }
    }

    private static bool TryReadOneBasedA0Loader(uint[] words, int start, out TmpkMapSelection selection)
    {
        selection = null!;
        if (!TmpkMipsOps.IsLui(words[start], 4, out ushort archiveUpper) || archiveUpper < 0x0100 || archiveUpper >= 0x8000 || start + 5 >= words.Length ||
            !TmpkMipsOps.IsLui(words[start + 1], 2, out ushort loaderUpper) || loaderUpper < 0x8000 ||
            !TmpkMipsOps.IsLoadWord(words[start + 2], 2, 2, 0x4F5C) ||
            words[start + 3] != 0 ||
            !TmpkMipsOps.IsJalr(words[start + 4], 2) ||
            !TmpkMipsOps.IsOri(words[start + 5], 4, 4, out ushort oneBasedEntry) ||
            oneBasedEntry == 0)
        {
            return false;
        }

        selection = new TmpkMapSelection(oneBasedEntry - 1, null, null);
        return true;
    }

    private static bool TryReadFileIdentifier(uint[] words, int start, out int end, out ushort archive, out int entry)
    {
        end = start;
        archive = 0;
        entry = 0;
        if (start >= words.Length || !TmpkMipsOps.IsLui(words[start], 5, out archive))
        {
            return false;
        }

        if (TmpkTriggerSequenceReader.TryReadEntryAtCall(words, start + 1, Math.Min(words.Length, start + MaximumSequenceWords), out entry))
        {
            end = start;
            return true;
        }

        end = start;
        entry = 0;
        return true;
    }
}
