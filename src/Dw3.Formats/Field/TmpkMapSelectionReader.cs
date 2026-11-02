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
    /// <param name="overlay">Raw bytes of the stage overlay that loads the TMPK maps.</param>
    /// <returns>The recovered pair of TMPK archive entry indices referenced by the overlay.</returns>
    /// <exception cref="FormatException">The overlay is not a valid MIPS instruction stream, or the expected two-map selection sequence is not found.</exception>
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

    /// <summary>Matches the three candidate setup patterns and returns the one that applies, or null.</summary>
    /// <param name="words">Decoded overlay instruction words.</param>
    /// <param name="start">Word index to test as the first instruction of a setup sequence.</param>
    /// <returns>A TmpkMapSelection when any pattern matches; null when none applies.</returns>
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

    /// <summary>
    /// Attempts to match a one-based map-selection sequence starting at <paramref name="start"/>
    /// using the $a1 register (map-index direct form): lui+ori load of the archive address,
    /// followed by addiu for the one-based entry, stored into the overlay's data section.
    /// </summary>
    /// <param name="words">Overlay instruction stream as u32 words.</param>
    /// <param name="start">Word index of the first instruction to test.</param>
    /// <param name="selection">Output selection when the method returns true.</param>
    /// <returns><see langword="true"/> when the sequence matches the expected pattern; otherwise false.</returns>
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

    /// <summary>Returns true when the A1-descriptor jalr pattern matches and builds the selection.</summary>
    /// <param name="words">Decoded overlay instruction words.</param>
    /// <param name="index">Index of the control-flow instruction that ended the A1 sequence scan.</param>
    /// <param name="storedFileIdentifier">True when a file-identifier store was seen before this instruction.</param>
    /// <param name="oneBasedEntry">One-based entry number read from the A1 register load.</param>
    /// <param name="selection">Receives the completed selection when the pattern matches.</param>
    /// <returns>True when the instruction is jalr on register 2 and the file identifier was stored.</returns>
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

    /// <summary>Classifies one A1-descriptor instruction, updating the state flags.</summary>
    /// <param name="words">Decoded overlay instruction words.</param>
    /// <param name="index">Index of the instruction to classify.</param>
    /// <param name="limit">Exclusive upper bound of the sequence window.</param>
    /// <param name="initializedDescriptor">Set to true when a lui+addiu descriptor pointer is found.</param>
    /// <param name="storedFileIdentifier">Set to true when a sw file-identifier store is found.</param>
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

    /// <summary>Reads the A0-loader two-instruction form and produces a one-based entry selection.</summary>
    /// <param name="words">Decoded overlay instruction words.</param>
    /// <param name="start">Start index to match from.</param>
    /// <param name="selection">Receives the floor selection when the pattern is found.</param>
    /// <returns>True when the six-instruction A0-loader pattern is matched.</returns>
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

    /// <summary>Reads the lui+call pattern that identifies the archive and floor entry numbers.</summary>
    /// <param name="words">Decoded overlay instruction words.</param>
    /// <param name="start">Index of the lui instruction that loads the archive upper half.</param>
    /// <param name="end">Receives the index of the first word after the matched pattern.</param>
    /// <param name="archive">Receives the archive upper-half constant.</param>
    /// <param name="entry">Receives the one-based floor entry number when present; 0 otherwise.</param>
    /// <returns>True when a lui instruction is found at start; false if words runs out.</returns>
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
