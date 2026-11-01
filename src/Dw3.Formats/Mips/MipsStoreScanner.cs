namespace Dw3.Formats.Mips;

/// <summary>
/// Finds the values a code buffer stores into one field of a structure at a fixed address,
/// such as the setup routine of a disc overlay filling a global state record.
/// </summary>
public static class MipsStoreScanner
{
    /// <summary>
    /// Scans every whole word of <paramref name="code"/> with a <see cref="MipsRegisterTracker"/> and
    /// returns, in code order, each store to <paramref name="structureAddress"/> +
    /// <paramref name="fieldOffset"/> whose value is known.
    /// </summary>
    /// <param name="watchedLoads">Word addresses whose loads are kept symbolic (see <see cref="MipsValue.LoadedWord"/>).</param>
    public static IReadOnlyList<MipsStoreMatch> FindStores(
        ReadOnlySpan<byte> code,
        uint structureAddress,
        short fieldOffset,
        IEnumerable<uint>? watchedLoads = null)
    {
        uint target = unchecked(structureAddress + (uint)fieldOffset);
        var tracker = new MipsRegisterTracker(watchedLoads);
        var matches = new List<MipsStoreMatch>();
        foreach (MipsInstruction instruction in MipsInstruction.DecodeRange(code, 0, MipsInstruction.WholeWordEnd(code.Length)))
        {
            if (tracker.Execute(instruction) is MipsStore store && store.Address == target)
                matches.Add(new MipsStoreMatch(store.Offset, store.Value));
        }
        return matches;
    }
}

/// <summary>A store to the requested field: the <c>sw</c> byte offset and the stored value.</summary>
public readonly record struct MipsStoreMatch(int Offset, MipsValue Value);
