namespace Dw3.Formats.Field;

/// <summary>Locates and reads the trigger-sequence TMPK entry in a MIPS stage overlay.</summary>
internal static class TmpkTriggerSequenceReader
{
    /// <summary>Searches for a trigger-sequence call and returns its entry index when found.</summary>
    /// <param name="words">Decoded overlay instruction words.</param>
    /// <param name="start">First word index to search from.</param>
    /// <param name="archive">Expected archive upper half to confirm a trigger call.</param>
    /// <param name="maximumSequenceWords">Maximum words to search before giving up.</param>
    /// <param name="trigger">Receives the trigger entry index when found.</param>
    /// <returns>True when a trigger-sequence call is found; false otherwise.</returns>
    public static bool TryFind(uint[] words, int start, ushort archive, int maximumSequenceWords, out int trigger)
    {
        int limit = Math.Min(words.Length, start + maximumSequenceWords);
        for (int index = start; index < limit; index++)
        {
            if (!TmpkMipsOps.IsAddiu(words[index], 4, 0, 7))
            {
                continue;
            }

            if (index + 2 < words.Length && TmpkMipsOps.IsControlFlow(words[index + 1]))
            {
                TmpkMipsOps.RejectControlFlow(words[index + 1], index + 1);
            }

            if (index + 1 >= words.Length || !TmpkMipsOps.IsLui(words[index + 1], 5, out ushort candidateArchive))
            {
                continue;
            }

            if (!TryReadEntryAtCall(words, index + 2, limit, out trigger))
            {
                continue;
            }

            if (candidateArchive != archive)
            {
                continue;
            }

            return true;
        }

        trigger = 0;
        return false;
    }

    /// <summary>Scans forward from <paramref name="start"/> for an ORI or sll+ORI that sets register 5.</summary>
    /// <param name="words">Decoded overlay instruction words.</param>
    /// <param name="start">First word index to scan.</param>
    /// <param name="limit">Exclusive upper bound of words to scan.</param>
    /// <param name="entry">Receives the entry index when found.</param>
    /// <returns>True when a matching entry load followed by jalr is found.</returns>
    public static bool TryReadEntryAtCall(uint[] words, int start, int limit, out int entry)
    {
        for (int index = start; index < limit; index++)
        {
            if (TmpkMipsOps.IsAddiuToRegister(words[index], 4, 0))
            {
                break;
            }

            TmpkMipsOps.RejectControlFlow(words[index], index, allowJalr: true);
            if (TmpkMipsOps.IsOri(words[index], 5, 5, out ushort direct))
            {
                entry = direct;
                return HasJalr(words, index + 1, limit);
            }

            if ((words[index] >> 26) == 0 && (words[index] & 0x3F) == 9 && index + 1 < words.Length && TmpkMipsOps.IsOri(words[index + 1], 5, 5, out ushort delayed))
            {
                entry = delayed;
                return true;
            }
        }

        entry = 0;
        return false;
    }

    /// <summary>Returns true when a jalr instruction appears before any disallowed control flow.</summary>
    /// <param name="words">Decoded overlay instruction words.</param>
    /// <param name="start">First word index to scan.</param>
    /// <param name="limit">Exclusive upper bound of words to scan.</param>
    /// <returns>True when a jalr is found within the range.</returns>
    private static bool HasJalr(uint[] words, int start, int limit)
    {
        for (int index = start; index < limit; index++)
        {
            TmpkMipsOps.RejectControlFlow(words[index], index, allowJalr: true);
            if ((words[index] >> 26) == 0 && (words[index] & 0x3F) == 9)
            {
                return true;
            }
        }

        return false;
    }
}
