using System.Buffers.Binary;
using Dw3.Formats.Encounter;
using Xunit;

namespace Dw3.Formats.Tests.Encounter;

/// <summary>Disc-free tests for the stage battle array scanner.</summary>
public sealed class StageBattlesScannerTests
{
    private const uint OverlayBase = 0x800A5DE0u;

    // Fabricate a minimal overlay where the setup code stores a FieldBattles pointer
    // to FIELDSTG_state.battles (0x80099D80 + 0x20 = 0x80099DA0).
    private const uint FieldStateAddress = 0x80099D80u;
    private const short BattlesFieldOffset = 0x20;

    /// <summary>
    /// Builds a synthetic overlay with a minimal valid FieldBattles array at the given data offset,
    /// plus a MIPS stub that stores its address to FIELDSTG_state.battles.
    /// Returns the overlay bytes and the expected array address.
    /// </summary>
    private static (byte[] Overlay, uint ArrayAddress, int ExpectedCount) BuildOverlayWithBattles(
        int battlesDataOffset, int entryCount)
    {
        // The overlay must be word-aligned; choose a size that fits the FieldBattles array
        // and some MIPS code at the start.
        // Layout: FieldBattles[] | padding | AreaBattles[0..n-1][8] | BattleLists[0..n-1]
        int areaBase = battlesDataOffset + entryCount * 28 + 0x100;
        int listBase = areaBase + entryCount * 8 * 12;  // after all entries' 8 AreaBattles
        int dataEnd  = listBase + entryCount * 36 + 0x100;
        int overlaySize = ((dataEnd + 3) / 4) * 4;
        byte[] overlay = new byte[overlaySize];

        uint arrayAddress = OverlayBase + (uint)battlesDataOffset;

        // Write entryCount valid FieldBattles entries at battlesDataOffset.
        for (int entry = 0; entry < entryCount; entry++)
        {
            int fb = battlesDataOffset + entry * 28;

            // Build 8 AreaBattles for this entry at areaBase + entry*8*12
            int areaOff = areaBase + entry * 8 * 12;
            for (int a = 0; a < 8; a++)
            {
                int ao = areaOff + a * 12;
                BinaryPrimitives.WriteInt32LittleEndian(overlay.AsSpan(ao), a);            // encounter
                BinaryPrimitives.WriteInt32LittleEndian(overlay.AsSpan(ao + 4), entry);   // stage
                BinaryPrimitives.WriteInt32LittleEndian(overlay.AsSpan(ao + 8),
                    unchecked((int)0x60040000u)); // music MUSIC(1,0)
            }

            // Build a BattleList at listBase + entry*36
            int blOff = listBase + entry * 36;
            BinaryPrimitives.WriteInt32LittleEndian(overlay.AsSpan(blOff), 0); // count=0
            for (int a = 0; a < 8; a++)
            {
                uint aPtr = OverlayBase + (uint)(areaOff + a * 12);
                BinaryPrimitives.WriteUInt32LittleEndian(overlay.AsSpan(blOff + 4 + a * 4), aPtr);
            }

            // Write FieldBattles
            BinaryPrimitives.WriteInt32LittleEndian(overlay.AsSpan(fb),     entry + 1);   // serial (non-zero)
            BinaryPrimitives.WriteInt32LittleEndian(overlay.AsSpan(fb + 4), entry);       // id
            BinaryPrimitives.WriteInt32LittleEndian(overlay.AsSpan(fb + 8), 0);           // pad8 = 0
            uint blPtr = OverlayBase + (uint)blOff;
            for (int a = 0; a < 4; a++)
                BinaryPrimitives.WriteUInt32LittleEndian(overlay.AsSpan(fb + 12 + a * 4), blPtr);
        }

        // MIPS stub: lui + addiu to load FIELDSTG_state address, then sw to store the battles pointer.
        // lui $t0, upper(FieldStateAddress)   [0x3D40_XXXX]
        // addiu $t0, $t0, lower(FieldStateAddress) [0x2108_XXXX]
        // lui $t1, upper(arrayAddress)
        // addiu $t1, $t1, lower(arrayAddress)
        // sw $t1, 0x20($t0)  → stores to FieldStateAddress+0x20 = 0x80099DA0
        // jr $ra
        // nop
        uint fsUpper = FieldStateAddress >> 16;
        int fsLower = unchecked((short)(FieldStateAddress & 0xFFFF));
        uint arrUpper = arrayAddress >> 16;
        int arrLower = unchecked((short)(arrayAddress & 0xFFFF));

        // Adjust for sign extension of addiu
        if ((FieldStateAddress & 0x8000) != 0) fsUpper++;
        if ((arrayAddress & 0x8000) != 0) arrUpper++;

        uint[] mips = [
            0x3C08_0000u | fsUpper,           // lui $t0, fsUpper
            0x2508_0000u | (uint)(ushort)fsLower,  // addiu $t0, $t0, fsLower
            0x3C09_0000u | arrUpper,          // lui $t1, arrUpper
            0x2529_0000u | (uint)(ushort)arrLower, // addiu $t1, $t1, arrLower
            0xAD09_0020u,                     // sw $t1, 0x20($t0)
            0x03E0_0008u,                     // jr $ra
            0x0000_0000u,                     // nop
        ];
        for (int i = 0; i < mips.Length; i++)
            BinaryPrimitives.WriteUInt32LittleEndian(overlay.AsSpan(i * 4), mips[i]);

        return (overlay, arrayAddress, entryCount);
    }

    [Fact]
    public void Scan_SingleEntryArray_FoundCorrectly()
    {
        var (overlay, arrayAddress, expectedCount) = BuildOverlayWithBattles(0x800, 1);

        IReadOnlyList<StageBattleArray> arrays = StageBattlesScanner.Scan(
            overlay, OverlayBase, FieldStateAddress, BattlesFieldOffset);

        Assert.Single(arrays);
        Assert.Equal(arrayAddress, arrays[0].Address);
        Assert.Equal(expectedCount, arrays[0].Count);
    }

    [Fact]
    public void Scan_TwoEntryArray_FindsBothEntries()
    {
        var (overlay, arrayAddress, expectedCount) = BuildOverlayWithBattles(0x800, 2);

        IReadOnlyList<StageBattleArray> arrays = StageBattlesScanner.Scan(
            overlay, OverlayBase, FieldStateAddress, BattlesFieldOffset);

        Assert.Single(arrays);
        Assert.Equal(arrayAddress, arrays[0].Address);
        Assert.Equal(2, arrays[0].Count);
    }

    [Fact]
    public void Scan_PatternA_TwoSequentialStores_SingleArrayTwoEntries()
    {
        // Pattern A: the MIPS code stores the base address THEN the element-1 address
        // sequentially (two sw instructions, same code path). Expects 1 array with count=2.
        var (overlay, arrayAddress, _) = BuildOverlayWithBattles(0x800, 2);
        uint elem1 = arrayAddress + FieldEncounterDecoder.FieldBattlesStride;

        // Overwrite MIPS stub: first store arrayAddress, then store elem1 address.
        // lui $t0, fsUpper / addiu $t0, fsLower  (FIELDSTG_state base)
        // lui $t1, arrUpper / addiu $t1, arrLower  (array base)
        // sw $t1, 0x20($t0)     ← first store (array base)
        // lui $t2, elem1Upper / addiu $t2, elem1Lower
        // sw $t2, 0x20($t0)     ← second store (element 1 pointer)
        // jr $ra / nop
        uint fsUpper = FieldStateAddress >> 16;
        uint fsLow   = (uint)(FieldStateAddress & 0xFFFF);
        uint arrUpper = arrayAddress >> 16;
        uint arrLow   = (uint)(arrayAddress & 0xFFFF);
        uint e1Upper = elem1 >> 16;
        uint e1Low   = (uint)(elem1 & 0xFFFF);
        if ((FieldStateAddress & 0x8000) != 0) fsUpper++;
        if ((arrayAddress   & 0x8000) != 0) arrUpper++;
        if ((elem1          & 0x8000) != 0) e1Upper++;

        uint[] mips =
        [
            0x3C08_0000u | fsUpper,  0x2508_0000u | (uint)(ushort)fsLow,   // $t0 = FIELDSTG_state
            0x3C09_0000u | arrUpper, 0x2529_0000u | (uint)(ushort)arrLow,  // $t1 = arrayBase
            0xAD09_0020u,                                                   // sw $t1, 0x20($t0)
            0x3C0A_0000u | e1Upper,  0x254A_0000u | (uint)(ushort)e1Low,   // $t2 = elem1
            0xAD0A_0020u,                                                   // sw $t2, 0x20($t0)
            0x03E0_0008u, 0x0000_0000u,                                     // jr $ra / nop
        ];
        for (int i = 0; i < mips.Length; i++)
            BinaryPrimitives.WriteUInt32LittleEndian(overlay.AsSpan(i * 4), mips[i]);

        IReadOnlyList<StageBattleArray> arrays = StageBattlesScanner.Scan(
            overlay, OverlayBase, FieldStateAddress, BattlesFieldOffset);

        // Must collapse into one array of count=2 (elem1 is an element pointer, not a separate array).
        Assert.Single(arrays);
        Assert.Equal(arrayAddress, arrays[0].Address);
        Assert.Equal(2, arrays[0].Count);
    }

    [Fact]
    public void Scan_PatternB_TwoAddressesMaterialized_TwoSeparateArrays()
    {
        // Pattern B: the MIPS code stores only the LAST of two valid array addresses
        // (simulating the last-taken branch). Strategy C materialises both. Each must be
        // returned as a separate 1-entry array rather than merged into one 2-entry array.
        //
        // Layout: two consecutive valid 1-entry FieldBattles entries at offsetA and offsetA+28.
        // MIPS only stores addrB (= addrA+28) directly; addrA is materialised but not stored.
        int offsetA = 0x800;
        int offsetB = offsetA + FieldEncounterDecoder.FieldBattlesStride;
        // Build a 2-entry overlay so both addrA and addrB have valid FieldBattles data.
        var (overlay, addrA, _) = BuildOverlayWithBattles(offsetA, 2);
        uint addrB = addrA + FieldEncounterDecoder.FieldBattlesStride;

        // Overwrite MIPS stub: store addrB to battles (confirmed), materialise addrA (Strategy C).
        uint fsUpper = FieldStateAddress >> 16;
        uint fsLow   = (uint)(FieldStateAddress & 0xFFFF);
        uint bUpper  = addrB >> 16;
        uint bLow    = (uint)(addrB & 0xFFFF);
        uint aUpper  = addrA >> 16;
        uint aLow    = (uint)(addrA & 0xFFFF);
        if ((FieldStateAddress & 0x8000) != 0) fsUpper++;
        if ((addrB & 0x8000) != 0) bUpper++;
        if ((addrA & 0x8000) != 0) aUpper++;

        uint[] mips =
        [
            0x3C08_0000u | fsUpper, 0x2508_0000u | (uint)(ushort)fsLow, // $t0 = FIELDSTG_state
            0x3C09_0000u | bUpper,  0x2529_0000u | (uint)(ushort)bLow,  // $t1 = addrB (stored)
            0xAD09_0020u,                                                 // sw $t1 → battles = addrB
            0x3C0A_0000u | aUpper,  0x254A_0000u | (uint)(ushort)aLow,  // $t2 = addrA (materialised)
            0x03E0_0008u, 0x0000_0000u,                                  // jr $ra / nop
        ];
        for (int i = 0; i < mips.Length; i++)
            BinaryPrimitives.WriteUInt32LittleEndian(overlay.AsSpan(i * 4), mips[i]);

        IReadOnlyList<StageBattleArray> arrays = StageBattlesScanner.Scan(
            overlay, OverlayBase, FieldStateAddress, BattlesFieldOffset);

        // Expect two separate 1-entry arrays (addrA from Strategy C, addrB from Strategy A).
        // addrA is capped at count=1 because addrB is the next candidate and 28/stride=1.
        // addrB (confirmed base) counts only 1 structurally valid entry (addrB+28 is invalid).
        Assert.Equal(2, arrays.Count);
        Assert.Contains(arrays, a => a.Address == addrA && a.Count == 1);
        Assert.Contains(arrays, a => a.Address == addrB && a.Count == 1);
    }

    [Fact]
    public void Scan_NoStores_ReturnsEmpty()
    {
        // An overlay that never stores to FIELDSTG_state.battles
        byte[] overlay = new byte[28];
        // nop-filled
        for (int i = 0; i < overlay.Length / 4; i++)
            BinaryPrimitives.WriteUInt32LittleEndian(overlay.AsSpan(i * 4), 0x0000_0000u);

        IReadOnlyList<StageBattleArray> arrays = StageBattlesScanner.Scan(
            overlay, OverlayBase, FieldStateAddress, BattlesFieldOffset);

        Assert.Empty(arrays);
    }
}
