using System.Buffers.Binary;
using Dw3.Formats.Binary;

namespace Dw3.Formats.Dialogue;

/// <summary>
/// The executable's file-sector table joined with the disc directory: maps an engine file id to the
/// ISO path of the disc member starting at that id's LBA.
/// </summary>
public sealed class DiscFileTable
{
    private readonly int[] _lbas;
    private readonly IReadOnlyDictionary<int, string> _pathByLba;

    private DiscFileTable(int[] lbas, IReadOnlyDictionary<int, string> pathByLba)
    {
        _lbas = lbas;
        _pathByLba = pathByLba;
    }

    /// <summary>Number of file ids in the table.</summary>
    public int Count => _lbas.Length;

    /// <summary>Reads <paramref name="count"/> little-endian u32 start LBAs at <paramref name="tableAddress"/>.</summary>
    /// <param name="pathByLba">ISO path of every disc member, keyed by its first LBA.</param>
    /// <exception cref="FormatException">The table is outside the executable image.</exception>
    public static DiscFileTable Read(PsxExecutable executable, uint tableAddress, int count, IReadOnlyDictionary<int, string> pathByLba)
    {
        ArgumentNullException.ThrowIfNull(executable);
        ArgumentNullException.ThrowIfNull(pathByLba);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        ReadOnlySpan<byte> table = executable.Slice(tableAddress, checked(count * sizeof(uint)));
        var lbas = new int[count];
        for (int id = 0; id < count; id++)
            lbas[id] = BinaryPrimitives.ReadInt32LittleEndian(table.Slice(id * sizeof(uint), sizeof(uint)));
        return new DiscFileTable(lbas, pathByLba);
    }

    /// <summary>ISO path of file <paramref name="fileId"/>, or null when no disc member starts at its LBA.</summary>
    /// <exception cref="FormatException">The id is outside the table.</exception>
    public string? PathOf(int fileId)
    {
        if ((uint)fileId >= (uint)_lbas.Length)
            throw new FormatException($"File id {fileId} is outside the {_lbas.Length}-entry file-sector table.");
        int lba = _lbas[fileId];
        return lba > 0 ? _pathByLba.GetValueOrDefault(lba) : null;
    }
}
