using System.Buffers.Binary;
using Xunit;

namespace Dw3.Extractor.Tests;

/// <summary>
/// Synthetic tests for DiscStageModeReader table-parsing logic.
/// All data is fabricated; no disc bytes are embedded.
/// </summary>
public sealed class DiscStageModeReaderTests
{
    [Fact]
    public void ParsesModesToStageNumbersFromSyntheticEntries()
    {
        byte[] fieldStgData = BuildFieldStgFragment(
        [
            (512, 10, unchecked((int)0x800A5E7C)),
            (513, 20, unchecked((int)0x800A5E2C)),
            (0,    0, 0),
        ]);

        int[] fileSectors = new int[30];
        fileSectors[10] = 500;
        fileSectors[20] = 600;
        var lbaToName = new Dictionary<int, string>
        {
            { 500, "WSTAG200.PRO" },
            { 600, "WSTAG202.PRO" },
        };

        IReadOnlyDictionary<ushort, int> result =
            DiscStageModeReader.ParseStageModes(fieldStgData, fileSectors, lbaToName, entryCount: 3, tableOffset: 0);

        Assert.Equal(2, result.Count);
        Assert.Equal(200, result[512]);
        Assert.Equal(202, result[513]);
    }

    [Fact]
    public void SkipsEntryWhenOverlayIndexIsOutOfRange()
    {
        byte[] fieldStgData = BuildFieldStgFragment(
        [
            (512, 9999, unchecked((int)0x800A0000)),
            (0,      0, 0),
        ]);
        int[] fileSectors = new int[10];
        var lbaToName = new Dictionary<int, string>();

        IReadOnlyDictionary<ushort, int> result =
            DiscStageModeReader.ParseStageModes(fieldStgData, fileSectors, lbaToName, entryCount: 2, tableOffset: 0);

        Assert.Empty(result);
    }

    [Fact]
    public void SkipsEntryWhenLbaIsNotAWstag()
    {
        byte[] fieldStgData = BuildFieldStgFragment(
        [
            (512, 5, unchecked((int)0x800A0000)),
            (0,   0, 0),
        ]);
        int[] fileSectors = new int[10];
        fileSectors[5] = 700;
        var lbaToName = new Dictionary<int, string>
        {
            { 700, "FIELDSTG.PRO" },
        };

        IReadOnlyDictionary<ushort, int> result =
            DiscStageModeReader.ParseStageModes(fieldStgData, fileSectors, lbaToName, entryCount: 2, tableOffset: 0);

        Assert.Empty(result);
    }

    [Fact]
    public void StopsAtSentinelModeZero()
    {
        byte[] fieldStgData = BuildFieldStgFragment(
        [
            (512, 1, unchecked((int)0x800A0000)),
            (0,   0, 0),
            (514, 2, unchecked((int)0x800A0000)),
        ]);
        int[] fileSectors = new int[10];
        fileSectors[1] = 500;
        fileSectors[2] = 600;
        var lbaToName = new Dictionary<int, string>
        {
            { 500, "WSTAG200.PRO" },
            { 600, "WSTAG205.PRO" },
        };

        IReadOnlyDictionary<ushort, int> result =
            DiscStageModeReader.ParseStageModes(fieldStgData, fileSectors, lbaToName, entryCount: 3, tableOffset: 0);

        Assert.Single(result);
        Assert.Equal(200, result[512]);
    }

    private static byte[] BuildFieldStgFragment(
        IEnumerable<(int mode, int overlayIndex, int funcPtr)> entries)
    {
        var list = entries.ToList();
        byte[] data = new byte[list.Count * 12];
        for (int i = 0; i < list.Count; i++)
        {
            int off = i * 12;
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(off, 4), list[i].mode);
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(off + 4, 4), list[i].overlayIndex);
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(off + 8, 4), list[i].funcPtr);
        }
        return data;
    }
}
