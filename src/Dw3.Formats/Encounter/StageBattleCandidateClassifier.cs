namespace Dw3.Formats.Encounter;

/// <summary>
/// Classifies stage battle array candidates by their relationship to confirmed stores.
/// </summary>
internal static class StageBattleCandidateClassifier
{
    private const int MaximumEntries = 30;

    /// <summary>
    /// Gets the global maximum array length.
    /// </summary>
    internal static int MaximumArrayLength => MaximumEntries;

    /// <summary>
    /// Finds confirmed addresses that are entries inside the first confirmed array.
    /// </summary>
    /// <param name="confirmed">Addresses directly confirmed by stores or calls.</param>
    /// <returns>Addresses that are element pointers rather than array bases.</returns>
    internal static HashSet<uint> FindElementPointers(ISet<uint> confirmed)
    {
        if (confirmed.Count == 0)
        {
            return [];
        }

        uint baseAddress = confirmed.Min();
        return [.. confirmed.Where(address => IsElementPointer(address, baseAddress))];
    }

    /// <summary>
    /// Calculates the maximum count allowed for an unconfirmed candidate.
    /// </summary>
    /// <param name="address">Candidate address.</param>
    /// <param name="nextAddress">Next candidate address.</param>
    /// <returns>Maximum record count before the next candidate.</returns>
    internal static int LimitUnconfirmed(uint address, uint nextAddress)
    {
        if (nextAddress <= address)
        {
            return MaximumEntries;
        }

        uint distance = nextAddress - address;
        return (int)Math.Min(distance / (uint)FieldEncounterDecoder.FieldBattlesStride, MaximumEntries);
    }

    /// <summary>
    /// Determines whether an address identifies a nonzero record inside the base array.
    /// </summary>
    /// <param name="address">Candidate RAM address to classify.</param>
    /// <param name="baseAddress">RAM address of the confirmed array's first record.</param>
    /// <returns><see langword="true"/> only for a later aligned record within the 30-entry cap.</returns>
    private static bool IsElementPointer(uint address, uint baseAddress)
    {
        long delta = (long)address - baseAddress;
        return delta > 0
            && (delta % FieldEncounterDecoder.FieldBattlesStride) == 0
            && (delta / FieldEncounterDecoder.FieldBattlesStride) < MaximumEntries;
    }
}
