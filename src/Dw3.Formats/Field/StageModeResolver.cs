using System.Buffers.Binary;

namespace Dw3.Formats.Field;

/// <summary>Builds the field-mode to stage lookup directly from each raw WSTAG overlay.</summary>
public static class StageModeResolver
{
    private const int ModeBeforeSlotTableBytes = 24;

    /// <summary>Maps every overlay's source mode to its stage identifier.</summary>
    /// <param name="overlays">Sequence of (stageId, overlayBytes) pairs from the disc.</param>
    /// <returns>Dictionary mapping source mode (u16) to stage identifier for every overlay that contains a valid slot table.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="overlays"/> is null.</exception>
    public static IReadOnlyDictionary<ushort, int> Build(IEnumerable<(int Stage, byte[] Overlay)> overlays)
    {
        ArgumentNullException.ThrowIfNull(overlays);
        var result = new Dictionary<ushort, int>();
        foreach (var (stage, overlay) in overlays)
        {
            StageSlotTable? table = StageSlotLocator.TryLocate(overlay);
            if (table is null || table.Offset < ModeBeforeSlotTableBytes)
            {
                continue;
            }

            uint rawMode = BinaryPrimitives.ReadUInt32LittleEndian(overlay.AsSpan(table.Offset - ModeBeforeSlotTableBytes, sizeof(uint)));
            if (rawMode > ushort.MaxValue)
            {
                continue;
            }

            ushort mode = (ushort)rawMode;

            // Several overlays embed borrowed slot tables. The first raw owner is the
            // authoritative mode binding. Later copies do not redefine the mode.
            if (!result.TryAdd(mode, stage) && result[mode] == stage)
            {
                continue;
            }
        }

        return result;
    }
}
