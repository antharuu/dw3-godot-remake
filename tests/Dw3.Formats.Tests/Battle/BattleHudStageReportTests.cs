using System.Buffers.Binary;
using Dw3.Formats.Battle;
using Xunit;

namespace Dw3.Formats.Tests.Battle;

/// <summary>
/// Tests for BattleStageDecoder, the battle HUD decoders and BattleReportDecoder.
/// All inputs are synthetic — no disc bytes are included.
/// </summary>
public sealed class BattleHudStageReportTests
{
    private static readonly int[,] PinnedAnimations =
    {
        { 7, 8, 9, 10, 9, 8, -1 },
        { 14, 15, 16, 15, -1, -1, -1 },
        { 11, 12, 13, 12, -1, -1, -1 },
        { 3, 4, 5, 6, 5, 4, -1 },
        { 25, 26, 27, 28, 27, 26, -1 },
        { 0, 1, 2, 1, -1, -1, -1 },
        { 17, 18, 19, 20, 19, 18, -1 },
        { 21, 22, 23, 24, 23, 22, -1 },
    };

    // ── BattleStageDecoder ────────────────────────────────────────────────────
    [Fact]
    public void StageManifest_OnlyListsTheBlendEquationAsUnsupported()
    {
        Assert.Equal(["exact PS1 ABR blend equations"], BattleStageJsonWriter.UnsupportedFeatures);
    }

    [Fact]
    public void ParseRecord_ValidRecord_ReturnsCorrectFields()
    {
        // Build a synthetic FightStageInfo record for stage 0.
        // Stride = 0x58 bytes.
        byte[] data = new byte[0x58];
        int off = 0; // stage 0

        // model: file 0x70A, entry 0x1A → packed u32 = 0x070A001A
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(off + 0x00), 0x070A001A);

        // motions: file 0x70A, entry 0x00 → 0x070A0000
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(off + 0x04), 0x070A0000);

        // music: -1
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(off + 0x08), -1);

        // bgColor: R=0, G=70, B=199
        data[off + 0x0C] = 0;
        data[off + 0x0D] = 70;
        data[off + 0x0E] = 199;

        // noBoundsBones: terminated after three source bone ids.
        data[off + 0x10] = 4;
        data[off + 0x11] = 9;
        data[off + 0x12] = 12;

        // light 0: dir=(0, 12800, 0), rgb=(128, 128, 128)
        int l0 = off + 0x18;
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(l0), 0);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(l0 + 4), 12800);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(l0 + 8), 0);
        data[l0 + 12] = 128;
        data[l0 + 13] = 128;
        data[l0 + 14] = 128;

        // ambient: 76, 76, 76
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(off + 0x48), 76);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(off + 0x4C), 76);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(off + 0x50), 76);

        BattleStageInfo info = BattleStageDecoder.ParseRecord(data, 0);

        Assert.Equal(0, info.StageId);
        Assert.Equal(0x070A, info.ModelFile);
        Assert.Equal(0x001A, info.ModelEntry);
        Assert.Equal(0x070A, info.MotionFile);
        Assert.Equal(0x0000, info.MotionEntry);
        Assert.Equal(-1, info.Music);
        Assert.Equal([0, 70, 199], info.BgColor);
        Assert.Equal(3, info.Lights.Count);
        Assert.Equal(0, info.Lights[0].DirX);
        Assert.Equal(12800, info.Lights[0].DirY);
        Assert.Equal(128, info.Lights[0].R);
        Assert.Equal([4, 9, 12], info.NoBoundsBones);
        Assert.Equal([76, 76, 76], info.Ambient);
    }

    [Fact]
    public void ParseRecord_StageIdOutOfRange_Throws()
    {
        byte[] data = new byte[0x58];
        Assert.Throws<FormatException>(() => BattleStageDecoder.ParseRecord(data, -1));
        Assert.Throws<FormatException>(() => BattleStageDecoder.ParseRecord(data, 1));
    }

    [Fact]
    public void ParseRecord_BufferTooShort_Throws()
    {
        byte[] tiny = new byte[5];
        Assert.Throws<FormatException>(() => BattleStageDecoder.ParseRecord(tiny, 0));
    }

    [Theory]
    [InlineData(27, 0x07AF, 0x001A, 0x0006)]
    [InlineData(47, 0x07C3, 0x0008, 0x0000)]
    public void ParseRecord_PopulatedDirectStageId_ReturnsThatRecord(
        int stageId,
        int expectedModelFile,
        int expectedModelEntry,
        int expectedMotionEntry)
    {
        byte[] data = new byte[57 * 0x58];
        for (int recordId = 0; recordId < 57; recordId++)
        {
            data[recordId * 0x58] = 1;
        }

        int offset = stageId * 0x58;
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset), (uint)((expectedModelFile << 16) | expectedModelEntry));
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset + 0x04), (uint)((expectedModelFile << 16) | expectedMotionEntry));

        BattleStageInfo info = BattleStageDecoder.ParseRecord(data, stageId);

        Assert.Equal(stageId, info.StageId);
        Assert.Equal(expectedModelFile, info.ModelFile);
        Assert.Equal(expectedModelEntry, info.ModelEntry);
        Assert.Equal(expectedModelFile, info.MotionFile);
        Assert.Equal(expectedMotionEntry, info.MotionEntry);
    }

    [Fact]
    public void GetPopulatedRecordCount_57RecordsFollowedByZeroPadding_Returns57()
    {
        byte[] data = new byte[60 * 0x58];
        for (int recordId = 0; recordId < 57; recordId++)
        {
            data[recordId * 0x58] = 1;
        }

        int count = BattleStageDecoder.GetPopulatedRecordCount(data);

        Assert.Equal(57, count);
    }

    [Fact]
    public void GetPopulatedRecordCount_NonMultipleTableLength_ThrowsClearly()
    {
        byte[] data = new byte[0x58 + 1];

        FormatException exception = Assert.Throws<FormatException>(() => BattleStageDecoder.GetPopulatedRecordCount(data));

        Assert.Contains("not a multiple", exception.Message);
    }

    [Fact]
    public void GetPopulatedRecordCount_PopulatedRecordAfterZeroPadding_ThrowsClearly()
    {
        byte[] data = new byte[3 * 0x58];
        data[0] = 1;
        data[2 * 0x58] = 1;

        FormatException exception = Assert.Throws<FormatException>(() => BattleStageDecoder.GetPopulatedRecordCount(data));

        Assert.Contains("after zero padding", exception.Message);
    }

    // ── HudSpriteSheetParser.Parse ───────────────────────────────────────────
    [Fact]
    public void ParseSheet_TruncatedData_Throws()
    {
        Assert.Throws<FormatException>(() => HudSpriteSheetParser.Parse([]));
        Assert.Throws<FormatException>(() => HudSpriteSheetParser.Parse(new byte[8]));
    }

    [Fact]
    public void ParseSheet_InvalidOffsets_Throws()
    {
        // parts_offset > ids_offset → invalid
        byte[] data = BuildMinimalSheet(partsOffset: 20, idsOffset: 16);
        Assert.Throws<FormatException>(() => HudSpriteSheetParser.Parse(data));
    }

    [Fact]
    public void ParseSheet_UnterminatedIdList_Throws()
    {
        // No 0xFF terminator
        byte[] data = BuildMinimalSheetNoTerminator();
        Assert.Throws<FormatException>(() => HudSpriteSheetParser.Parse(data));
    }

    [Fact]
    public void ParseSheet_ValidSheet_ReturnsExpectedCounts()
    {
        byte[] data = BuildValidSheet([5, 10, 15], numParts: 2);
        var (parts, ids, offsets) = HudSpriteSheetParser.Parse(data);
        Assert.Equal(2, parts.Count);
        Assert.Equal([5, 10, 15], ids);
        Assert.Equal(3, offsets.Count);
    }

    [Fact]
    public void RenderFrame_PreservesNegativePartOffsets()
    {
        ushort[][] pixels = [.. Enumerable.Range(0, HudTexturePage.PageHeight).Select(_ => new ushort[HudTexturePage.PageWidthWords])];
        for (int index = 0; index < 16; index++)
        {
            pixels[0][index] = 0x7FFF;
        }

        pixels[10][0] = 0x1111;
        var pages = new[] { new HudTexturePage([.. pixels.Select(row => (IReadOnlyList<ushort>)row)]) };
        var parts = new[] { new HudSpritePart(0, 10, 4, 2, 0, 0, 0) };
        byte[] frame = new byte[12];
        BinaryPrimitives.WriteInt16LittleEndian(frame, 1);
        BinaryPrimitives.WriteInt16LittleEndian(frame.AsSpan(6), 0);
        BinaryPrimitives.WriteInt16LittleEndian(frame.AsSpan(8), -40);
        BinaryPrimitives.WriteInt16LittleEndian(frame.AsSpan(10), -3);

        var (_, width, height, offsetX, offsetY, _) = HudFrameRenderer.RenderFrame(parts, frame, 0, pages);

        Assert.Equal((-40, -3, 4, 2), (offsetX, offsetY, width, height));
    }

    // ── HudRlenDecoder.Decompress ──────────────────────────────────────
    [Fact]
    public void DecompressRlen_BadMagic_Throws()
    {
        Assert.Throws<FormatException>(() => HudRlenDecoder.Decompress("NOPE"u8));
    }

    [Fact]
    public void DecompressRlen_RepeatRun_ProducesCorrectOutput()
    {
        // RLEN + target=3 + cmd=0x83 (repeat 3) + val=0xAB + cmd=0x00 (end)
        byte[] data = [82, 76, 69, 78, 3, 0, 0, 0, 0x83, 0xAB, 0x00];
        byte[] result = HudRlenDecoder.Decompress(data);
        Assert.Equal([0xAB, 0xAB, 0xAB], result);
    }

    [Fact]
    public void DecompressRlen_LiteralRun_ProducesCorrectOutput()
    {
        // RLEN + target=3 + cmd=0x03 (literal 3) + 3 bytes + end
        byte[] data = [82, 76, 69, 78, 3, 0, 0, 0, 0x03, 0x11, 0x22, 0x33, 0x00];
        byte[] result = HudRlenDecoder.Decompress(data);
        Assert.Equal([0x11, 0x22, 0x33], result);
    }

    [Fact]
    public void DecompressRlen_SizeMismatch_Throws()
    {
        // Declared size = 5 but only emits 3.
        byte[] data = [82, 76, 69, 78, 5, 0, 0, 0, 0x03, 0x11, 0x22, 0x33, 0x00];
        Assert.Throws<FormatException>(() => HudRlenDecoder.Decompress(data));
    }

    // ── HudColor.Ps1Channel ──────────────────────────────────────────
    [Theory]
    [InlineData(0, 0)]
    [InlineData(31, 255)]
    [InlineData(1, 8)] // 1*255/31 = 8
    [InlineData(16, 131)] // 16*255/31 = 131
    public void Ps1Channel_MatchesPythonBattleHudFormula(int value, byte expected)
    {
        // Python: value * 255 // 31  (integer division)
        Assert.Equal(expected, HudColor.Ps1Channel(value));
    }

    // ── BattleReportDecoder.ReadAnimations ────────────────────────────────────
    [Fact]
    public void ReadAnimations_ValidOverlay_ReturnsCorrectTable()
    {
        byte[] overlay = BuildOverlayWithPinnedAnimations();
        int[,] result = BattleReportDecoder.ReadAnimations(overlay);
        Assert.Equal(8, result.GetLength(0));
        Assert.Equal(7, result.GetLength(1));

        // Spot-check row 0: (7, 8, 9, 10, 9, 8, -1)
        Assert.Equal(7, result[0, 0]);
        Assert.Equal(-1, result[0, 6]);
    }

    [Fact]
    public void ReadAnimations_TruncatedOverlay_Throws()
    {
        byte[] overlay = BuildOverlayWithPinnedAnimations();

        // Shorten by 1 byte so the table doesn't fit.
        byte[] truncated = overlay[..^1];
        Assert.Throws<FormatException>(() => BattleReportDecoder.ReadAnimations(truncated));
    }

    [Fact]
    public void FramePosition_KnownId_ReturnsCorrectIndex()
    {
        IReadOnlyList<int> ids = [3, 7, 10, 15];
        Assert.Equal(0, BattleReportDecoder.FramePosition(ids, 3));
        Assert.Equal(2, BattleReportDecoder.FramePosition(ids, 10));
    }

    [Fact]
    public void FramePosition_MissingId_Throws()
    {
        IReadOnlyList<int> ids = [3, 7, 10];
        Assert.Throws<FormatException>(() => BattleReportDecoder.FramePosition(ids, 99));
    }

    [Fact]
    public void RequiredFrameRows_HasExpectedCounts()
    {
        var rows = BattleReportDecoder.RequiredFrameRows;
        Assert.Equal(38, rows.Count);

        // Frames 0..0x1C (0..28) → 1 row
        Assert.Equal(1, rows[0]);
        Assert.Equal(1, rows[0x1C]);

        // Frames 0x1D..0x23 (29..35) → 11 rows
        Assert.Equal(11, rows[0x1D]);
        Assert.Equal(11, rows[0x23]);

        // Frames 0x24, 0x25 → 4 rows
        Assert.Equal(4, rows[0x24]);
        Assert.Equal(4, rows[0x25]);
    }

    [Fact]
    public void DecodeFourBitTimValidSyntheticPagePreservesEveryRowWord()
    {
        byte[] tim = BuildFourBitTim();

        HudTexturePage page = HudTexturePageDecoder.DecodeFourBitTim(tim);

        Assert.Equal(HudTexturePage.PageHeight, page.Height);
        Assert.Equal(HudTexturePage.PageWidthWords, page.WidthWords);
        Assert.Equal((ushort)0x1234, page.Rows[0][0]);
        Assert.Equal((ushort)0xABCD, page.Rows[255][63]);
    }

    [Fact]
    public void DecodeFourBitTimWrongDimensionsThrowsFormatException()
    {
        byte[] tim = BuildFourBitTim();
        BinaryPrimitives.WriteUInt16LittleEndian(tim.AsSpan(16), 63);

        Assert.Throws<FormatException>(() => HudTexturePageDecoder.DecodeFourBitTim(tim));
    }

    [Fact]
    public void DecodeCursorBarValidSyntheticStripConvertsOpaqueAndTransparentPixels()
    {
        byte[] images = BuildCursorImages();

        (int width, int height, byte[] rgba) = HudCursorBarDecoder.Decode(images);

        Assert.Equal(156, width);
        Assert.Equal(12, height);
        Assert.Equal([0, 0, 0, 0], rgba[..4]);
        Assert.Equal([255, 255, 255, 255], rgba[4..8]);
    }

    [Fact]
    public void DecodeCursorBarTruncatedPixelsThrowsFormatException()
    {
        byte[] images = BuildCursorImages();
        Array.Resize(ref images, images.Length - 1);

        Assert.Throws<FormatException>(() => HudCursorBarDecoder.Decode(images));
    }

    // ── helpers ───────────────────────────────────────────────────────────────
    private static byte[] BuildFourBitTim()
    {
        int pixelBytes = HudTexturePage.PageWidthWords * HudTexturePage.PageHeight * sizeof(ushort);
        byte[] tim = new byte[20 + pixelBytes];
        BinaryPrimitives.WriteUInt32LittleEndian(tim, 0x10);
        BinaryPrimitives.WriteUInt32LittleEndian(tim.AsSpan(8), (uint)(12 + pixelBytes));
        BinaryPrimitives.WriteUInt16LittleEndian(tim.AsSpan(16), HudTexturePage.PageWidthWords);
        BinaryPrimitives.WriteUInt16LittleEndian(tim.AsSpan(18), HudTexturePage.PageHeight);
        BinaryPrimitives.WriteUInt16LittleEndian(tim.AsSpan(20), 0x1234);
        BinaryPrimitives.WriteUInt16LittleEndian(tim.AsSpan(tim.Length - 2), 0xABCD);
        return tim;
    }

    private static byte[] BuildCursorImages()
    {
        const int cursorOffset = 8;
        const int width = 156;
        const int height = 12;
        int pixelBytes = width * height * sizeof(ushort);
        byte[] images = new byte[cursorOffset + 20 + pixelBytes];
        BinaryPrimitives.WriteUInt32LittleEndian(images, cursorOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(images.AsSpan(4), (uint)images.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(images.AsSpan(cursorOffset), 0x10);
        BinaryPrimitives.WriteUInt16LittleEndian(images.AsSpan(cursorOffset + 16), width);
        BinaryPrimitives.WriteUInt16LittleEndian(images.AsSpan(cursorOffset + 18), height);
        BinaryPrimitives.WriteUInt16LittleEndian(images.AsSpan(cursorOffset + 22), 0x7FFF);
        return images;
    }

    private static byte[] BuildMinimalSheet(int partsOffset, int idsOffset)
    {
        // 4-byte skip + header (partsOffset + idsOffset + at least one frame offset)
        int bodySize = Math.Max(idsOffset + 2, 20);
        byte[] data = new byte[4 + bodySize];
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), (uint)partsOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8), (uint)idsOffset);

        // Put 0xFF terminator at idsOffset (within sheet body).
        if (4 + idsOffset < data.Length)
        {
            data[4 + idsOffset] = 0xFF;
        }

        return data;
    }

    private static byte[] BuildMinimalSheetNoTerminator()
    {
        // Sheet with ids but no 0xFF terminator — fills with non-0xFF bytes.
        byte[] data = new byte[64];
        int partsOffset = 16, idsOffset = 20;
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), (uint)partsOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8), (uint)idsOffset);

        // Fill ids area with 0x01 but never 0xFF.
        for (int i = 4 + idsOffset; i < data.Length; i++)
        {
            data[i] = 1;
        }

        return data;
    }

    private static byte[] BuildValidSheet(int[] frameIds, int numParts)
    {
        // Layout: [skip4][partsOff4][idsOff4][frameOffsets...][partsTable][idList+0xFF][frameData]
        int numFrames = frameIds.Length;
        int headerBytes = 8 + (numFrames * 4); // partsOff + idsOff + frame offsets
        int partsTableBytes = numParts * 14;
        int partsOff = headerBytes;
        int idsOff = partsOff + partsTableBytes;
        int idsBytes = numFrames + 1; // ids + 0xFF
        int frameDataStart = idsOff + idsBytes;

        // Each frame record: count(2) + frameRow(2) + abr(2) + 1 part(6) = 12 bytes
        int frameSize = 12;
        int totalBody = frameDataStart + (numFrames * frameSize);

        byte[] data = new byte[4 + totalBody];
        _ = data.AsSpan(4); // body
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), (uint)partsOff);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8), (uint)idsOff);

        // Frame offsets (pointing into body).
        for (int i = 0; i < numFrames; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4 + 8 + (i * 4)), (uint)(frameDataStart + (i * frameSize)));
        }

        // Parts table (numParts × 14 bytes, valid but zeroed — mode 0, w/h = 0).
        // Set width and height to 1 to pass validation.
        for (int p = 0; p < numParts; p++)
        {
            int at = 4 + partsOff + (p * 14);
            BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(at + 4), 1); // width = 1
            BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(at + 6), 1); // height = 1
        }

        // Id list + terminator.
        for (int i = 0; i < numFrames; i++)
        {
            data[4 + idsOff + i] = (byte)frameIds[i];
        }

        data[4 + idsOff + numFrames] = 0xFF;

        // Frame records: count=1, frameRow=0, abr=0, part=0, dx=0, dy=0.
        for (int i = 0; i < numFrames; i++)
        {
            int at = 4 + frameDataStart + (i * frameSize);
            BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(at), 1);     // count=1

            // frameRow=0, abr=0, part=0, dx=0, dy=0 → all zeroed
        }

        return data;
    }

    private static byte[] BuildOverlayWithPinnedAnimations()
    {
        int offset = (int)(BattleReportDecoder.AnimationsAddr - BattleReportDecoder.OverlayBase);
        int size = 8 * 7 * 4;
        byte[] overlay = new byte[offset + size];
        for (int p = 0; p < 8; p++)
        {
            for (int f = 0; f < 7; f++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(
                    overlay.AsSpan(offset + (((p * 7) + f) * 4)), PinnedAnimations[p, f]);
            }
        }

        return overlay;
    }
}
