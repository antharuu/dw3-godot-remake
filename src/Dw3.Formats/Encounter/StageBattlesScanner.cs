namespace Dw3.Formats.Encounter;

/// <summary>
/// Discovers FieldBattles arrays in a stage overlay through stores, call arguments, and materialized addresses.
/// </summary>
public static class StageBattlesScanner
{
    /// <summary>
    /// Scans an overlay for structurally valid FieldBattles arrays.
    /// </summary>
    /// <param name="overlay">Raw WSTAG overlay bytes.</param>
    /// <param name="overlayBase">RAM load address of the overlay.</param>
    /// <param name="fieldStateAddress">RAM address of FIELDSTG_state.</param>
    /// <param name="battlesFieldOffset">Offset of the battles pointer in FIELDSTG_state.</param>
    /// <returns>All discovered battle arrays.</returns>
    public static IReadOnlyList<StageBattleArray> Scan(
        ReadOnlySpan<byte> overlay,
        uint overlayBase,
        uint fieldStateAddress,
        short battlesFieldOffset)
    {
        HashSet<uint> confirmed = StageBattleCandidates.CollectDirect(overlay, fieldStateAddress, battlesFieldOffset, overlayBase);
        AddCallCandidatesWhenNeeded(overlay, overlayBase, confirmed);
        uint[] candidates = CollectCandidates(overlay, overlayBase, confirmed);
        HashSet<uint> elementPointers = StageBattleCandidateClassifier.FindElementPointers(confirmed);
        return DecodeArrays(overlay, overlayBase, confirmed, candidates, elementPointers);
    }

    /// <summary>
    /// Adds first-argument candidates where direct stores were not found.
    /// </summary>
    /// <param name="overlay">Raw WSTAG overlay bytes containing candidate call sites.</param>
    /// <param name="overlayBase">RAM address at which <paramref name="overlay"/> is loaded.</param>
    /// <param name="confirmed">Candidate addresses proven by a direct store.</param>
    private static void AddCallCandidatesWhenNeeded(ReadOnlySpan<byte> overlay, uint overlayBase, HashSet<uint> confirmed)
    {
        if (confirmed.Count == 0)
        {
            confirmed.UnionWith(StageBattleCandidates.CollectCallArguments(overlay, overlayBase));
        }
    }

    /// <summary>
    /// Combines confirmed and instruction-materialized address candidates in address order.
    /// </summary>
    /// <param name="overlay">Raw WSTAG overlay bytes containing materialized addresses.</param>
    /// <param name="overlayBase">RAM address at which <paramref name="overlay"/> is loaded.</param>
    /// <param name="confirmed">Candidate addresses proven by a store or call argument.</param>
    /// <returns>Distinct candidate RAM addresses sorted in ascending order.</returns>
    private static uint[] CollectCandidates(ReadOnlySpan<byte> overlay, uint overlayBase, HashSet<uint> confirmed)
    {
        var candidates = new HashSet<uint>(confirmed);
        candidates.UnionWith(StageBattleCandidates.CollectMaterialized(overlay, overlayBase));
        return [.. candidates.OrderBy(address => address)];
    }

    /// <summary>
    /// Decodes every candidate that begins a unique valid array.
    /// </summary>
    /// <param name="overlay">Raw WSTAG overlay bytes.</param>
    /// <param name="overlayBase">RAM address at which <paramref name="overlay"/> is loaded.</param>
    /// <param name="confirmed">Addresses with direct evidence from the overlay code.</param>
    /// <param name="candidates">All candidate array starts in ascending RAM-address order.</param>
    /// <param name="elementPointers">Candidate addresses known to point inside another array.</param>
    /// <returns>Only valid arrays whose bases are not subsumed by another decoded array.</returns>
    private static List<StageBattleArray> DecodeArrays(
        ReadOnlySpan<byte> overlay,
        uint overlayBase,
        HashSet<uint> confirmed,
        uint[] candidates,
        HashSet<uint> elementPointers)
    {
        var arrays = new List<StageBattleArray>();
        var subsumed = new HashSet<uint>();
        for (int index = 0; index < candidates.Length; index++)
        {
            uint address = candidates[index];
            if (subsumed.Contains(address) || elementPointers.Contains(address))
            {
                continue;
            }

            int maximumEntries = GetMaximumEntries(confirmed, candidates, index);
            int count = StageBattleRecordValidator.CountValidEntries(overlay, address, overlayBase, maximumEntries);
            if (count == 0)
            {
                continue;
            }

            arrays.Add(DecodeArray(overlay, address, overlayBase, count));
            MarkSubsumedEntries(subsumed, address, count);
        }

        return arrays;
    }

    /// <summary>
    /// Calculates a count cap according to candidate confirmation status.
    /// </summary>
    /// <param name="confirmed">Addresses directly confirmed by a store or call.</param>
    /// <param name="candidates">Sorted address candidates that bound unconfirmed arrays.</param>
    /// <param name="index">Index of the candidate to limit.</param>
    /// <returns>The maximum records the candidate may contain before another candidate begins.</returns>
    private static int GetMaximumEntries(HashSet<uint> confirmed, uint[] candidates, int index)
    {
        if (confirmed.Contains(candidates[index]))
        {
            return StageBattleCandidateClassifier.MaximumArrayLength;
        }

        uint nextAddress = index + 1 < candidates.Length ? candidates[index + 1] : uint.MaxValue;
        return StageBattleCandidateClassifier.LimitUnconfirmed(candidates[index], nextAddress);
    }

    /// <summary>
    /// Decodes the specified number of FieldBattles records into an array value.
    /// </summary>
    /// <param name="overlay">Raw WSTAG overlay bytes.</param>
    /// <param name="address">RAM address of the first 12-byte FieldBattles record.</param>
    /// <param name="overlayBase">RAM address at which <paramref name="overlay"/> is loaded.</param>
    /// <param name="count">Validated number of consecutive records to decode.</param>
    /// <returns>The decoded records preserving their source array address and count.</returns>
    private static StageBattleArray DecodeArray(ReadOnlySpan<byte> overlay, uint address, uint overlayBase, int count)
    {
        var places = new List<FieldBattles>(count);
        for (int index = 0; index < count; index++)
        {
            uint entryAddress = address + (uint)(index * FieldEncounterDecoder.FieldBattlesStride);
            places.Add(FieldEncounterDecoder.DecodeFieldBattles(overlay, entryAddress, overlayBase));
        }

        return new StageBattleArray(address, count, places);
    }

    /// <summary>
    /// Marks records inside a decoded array so they are not emitted independently.
    /// </summary>
    /// <param name="subsumed">Addresses already known to belong to a decoded array.</param>
    /// <param name="address">RAM address of the decoded array's first record.</param>
    /// <param name="count">Number of 12-byte records in the decoded array.</param>
    private static void MarkSubsumedEntries(HashSet<uint> subsumed, uint address, int count)
    {
        for (int index = 1; index < count; index++)
        {
            subsumed.Add(address + (uint)(index * FieldEncounterDecoder.FieldBattlesStride));
        }
    }
}
