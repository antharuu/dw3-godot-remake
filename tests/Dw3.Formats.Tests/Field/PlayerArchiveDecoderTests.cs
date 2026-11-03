using System.Buffers.Binary;
using Dw3.Formats.Field;
using Xunit;

namespace Dw3.Formats.Tests.Field;

/// <summary>
/// xUnit tests for PlayerArchiveDecoder using only synthetic data.
/// No bytes from the game disc are used anywhere in this file.
/// </summary>
public sealed class PlayerArchiveDecoderTests
{
    // ── Helpers ────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a synthetic archive with <paramref name="entryCount"/> entries
    /// whose offset table is valid (ascending, within bounds, no gaps).
    /// Each entry payload is 4 zero-bytes.
    /// </summary>
    private static byte[] BuildMinimalArchive(int entryCount)
    {
        int tableBytes = entryCount * 4;
        int payloadPerEntry = 4;
        int totalSize = tableBytes + (entryCount * payloadPerEntry);
        var data = new byte[totalSize];
        for (int i = 0; i < entryCount; i++)
        {
            int offset = tableBytes + (i * payloadPerEntry);
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(i * 4, 4), (uint)offset);
        }

        return data;
    }

    /// <summary>
    /// Writes a u16 little-endian to an existing byte array at a given position.
    /// </summary>
    private static void WriteU16LE(byte[] buf, int pos, ushort value) =>
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(pos, 2), value);

    /// <summary>
    /// Writes an i32 little-endian to an existing byte array at a given position.
    /// </summary>
    private static void WriteI32LE(byte[] buf, int pos, int value) =>
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(pos, 4), value);

    /// <summary>
    /// Builds a minimal 4-bit TIM (flags=0x08) with a given pixel size.
    /// All pixels use CLUT index 1 (opaque magenta, word=0x7C1F) except
    /// a specified set of pixels that use CLUT index 0 (transparent, word=0).
    /// </summary>
    private static byte[] BuildTim4Bit(int widthWords, int height, HashSet<(int x, int y)>? transparentPixels = null)
    {
        int width = widthWords * 4;

        // CLUT: 16 entries, one row. Entry 0 = transparent, entry 1 = magenta.
        int clutDataBytes = 16 * 2;
        uint clutBlockLen = (uint)(12 + clutDataBytes);
        int pixelBytes = width * height / 2;
        uint imgBlockLen = (uint)(12 + pixelBytes);
        int total = 8 + (int)clutBlockLen + (int)imgBlockLen;
        var tim = new byte[total];
        int pos = 0;
        BinaryPrimitives.WriteUInt32LittleEndian(tim.AsSpan(pos, 4), 0x10u);
        pos += 4; // magic
        BinaryPrimitives.WriteUInt32LittleEndian(tim.AsSpan(pos, 4), 0x08u);
        pos += 4; // flags=4bit+CLUT

        // CLUT block
        BinaryPrimitives.WriteUInt32LittleEndian(tim.AsSpan(pos, 4), clutBlockLen);
        pos += 4;
        BinaryPrimitives.WriteUInt16LittleEndian(tim.AsSpan(pos, 2), 0);
        pos += 2; // vram x
        BinaryPrimitives.WriteUInt16LittleEndian(tim.AsSpan(pos, 2), 0);
        pos += 2; // vram y
        BinaryPrimitives.WriteUInt16LittleEndian(tim.AsSpan(pos, 2), 16);
        pos += 2; // 16 colors
        BinaryPrimitives.WriteUInt16LittleEndian(tim.AsSpan(pos, 2), 1);
        pos += 2;  // 1 row

        // Entry 0: 0x0000 (transparent)
        BinaryPrimitives.WriteUInt16LittleEndian(tim.AsSpan(pos, 2), 0x0000);
        pos += 2;

        // Entry 1: 0x7C1F (R=31,G=0,B=31, opaque magenta in 5-5-5 BGR)
        BinaryPrimitives.WriteUInt16LittleEndian(tim.AsSpan(pos, 2), 0x7C1F);
        pos += 2;

        // Remaining entries: 0 (transparent)
        pos += 14 * 2;

        // Image block
        BinaryPrimitives.WriteUInt32LittleEndian(tim.AsSpan(pos, 4), imgBlockLen);
        pos += 4;
        BinaryPrimitives.WriteUInt16LittleEndian(tim.AsSpan(pos, 2), 0);
        pos += 2; // vram x
        BinaryPrimitives.WriteUInt16LittleEndian(tim.AsSpan(pos, 2), 0);
        pos += 2; // vram y
        BinaryPrimitives.WriteUInt16LittleEndian(tim.AsSpan(pos, 2), (ushort)widthWords);
        pos += 2;
        BinaryPrimitives.WriteUInt16LittleEndian(tim.AsSpan(pos, 2), (ushort)height);
        pos += 2;

        // Pixel data: low nibble = left pixel, high nibble = right pixel.
        // Default: all index 1 (opaque).
        for (int y = 0; y < height; y++)
        {
            for (int xPair = 0; xPair < width / 2; xPair++)
            {
                int xLeft = xPair * 2;
                int xRight = (xPair * 2) + 1;
                bool tLeft = transparentPixels?.Contains((xLeft, y)) ?? false;
                bool tRight = transparentPixels?.Contains((xRight, y)) ?? false;
                byte lo = (byte)(tLeft ? 0 : 1);
                byte hi = (byte)(tRight ? 0 : 1);
                tim[pos++] = (byte)(lo | (hi << 4));
            }
        }

        return tim;
    }

    // ── DecodeOffsetTable ──────────────────────────────────────────────────
    [Fact]
    public void DecodeOffsetTable_ValidThreeEntry_ReturnsCorrectOffsets()
    {
        // 3 entries, table = 12 bytes; payloads start at 12
        var data = BuildMinimalArchive(3);

        var offsets = PlayerArchiveDecoder.DecodeOffsetTable(data, 2);

        Assert.Equal(3, offsets.Length);
        Assert.Equal(12, offsets[0]); // first payload = 12 (right after table)
        Assert.Equal(16, offsets[1]);
        Assert.Equal(20, offsets[2]);
    }

    [Fact]
    public void DecodeOffsetTable_DataTooShort_ThrowsFormatException()
    {
        // Request 5 entries but data has only 3 entries worth of table
        var data = BuildMinimalArchive(3);

        Assert.Throws<FormatException>(() => PlayerArchiveDecoder.DecodeOffsetTable(data, 4));
    }

    [Fact]
    public void DecodeOffsetTable_OffsetOutsidePayload_ThrowsFormatException()
    {
        var data = BuildMinimalArchive(3);

        // Corrupt offset[1] to point outside the data
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4, 4), (uint)data.Length + 10);

        Assert.Throws<FormatException>(() => PlayerArchiveDecoder.DecodeOffsetTable(data, 2));
    }

    [Fact]
    public void DecodeOffsetTable_OffsetBelowTableEnd_ThrowsFormatException()
    {
        var data = BuildMinimalArchive(3);

        // Corrupt offset[0] to point into the table itself (offset = 4, table ends at 12)
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0, 4), 4u);

        Assert.Throws<FormatException>(() => PlayerArchiveDecoder.DecodeOffsetTable(data, 2));
    }

    [Fact]
    public void DecodeOffsetTable_NonAscendingOffsets_ThrowsFormatException()
    {
        var data = BuildMinimalArchive(3);

        // Swap offsets[0] and offsets[1] to make them non-ascending
        uint first = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(0, 4));
        uint second = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4, 4));
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0, 4), second);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4, 4), first);

        Assert.Throws<FormatException>(() => PlayerArchiveDecoder.DecodeOffsetTable(data, 2));
    }

    // ── ComputeVisibleBbox ────────────────────────────────────────────────
    [Fact]
    public void ComputeVisibleBbox_AllOpaquePixels_ReturnsFullBounds()
    {
        // 4×2 image, all opaque (index 1 in CLUT): bbox covers the full image
        var timBytes = BuildTim4Bit(widthWords: 1, height: 2); // 4×2 image
        var tim = Dw3.Formats.Binary.Tim.TimDecoder.Decode(timBytes);

        var bbox = PlayerArchiveDecoder.ComputeVisibleBbox(tim);

        // left=0, upper=0, right_excl=4, lower_excl=2
        Assert.Equal(0, bbox.Left);
        Assert.Equal(0, bbox.Upper);
        Assert.Equal(4, bbox.RightExclusive);
        Assert.Equal(2, bbox.LowerExclusive);
    }

    [Fact]
    public void ComputeVisibleBbox_TopRowTransparent_BboxStartsAtRow1()
    {
        // 4×4 image, top row is transparent (y=0 all transparent)
        var transparent = new HashSet<(int, int)> { (0, 0), (1, 0), (2, 0), (3, 0) };
        var timBytes = BuildTim4Bit(widthWords: 1, height: 4, transparent);
        var tim = Dw3.Formats.Binary.Tim.TimDecoder.Decode(timBytes);

        var bbox = PlayerArchiveDecoder.ComputeVisibleBbox(tim);

        Assert.Equal(0, bbox.Left);
        Assert.Equal(1, bbox.Upper);      // first non-transparent row is y=1
        Assert.Equal(4, bbox.RightExclusive);
        Assert.Equal(4, bbox.LowerExclusive);
    }

    [Fact]
    public void ComputeVisibleBbox_OnlySinglePixelOpaque_ReturnsTightBounds()
    {
        // 4×2 image, only pixel (2, 1) is opaque; everything else transparent
        var transparent = new HashSet<(int, int)>();
        for (int y = 0; y < 2; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                if (!(x == 2 && y == 1))
                {
                    transparent.Add((x, y));
                }
            }
        }

        var timBytes = BuildTim4Bit(widthWords: 1, height: 2, transparent);
        var tim = Dw3.Formats.Binary.Tim.TimDecoder.Decode(timBytes);

        var bbox = PlayerArchiveDecoder.ComputeVisibleBbox(tim);

        Assert.Equal(2, bbox.Left);
        Assert.Equal(1, bbox.Upper);
        Assert.Equal(3, bbox.RightExclusive);  // maxX+1 = 2+1 = 3
        Assert.Equal(2, bbox.LowerExclusive);  // maxY+1 = 1+1 = 2
    }

    [Fact]
    public void ComputeVisibleBbox_AllTransparent_ThrowsFormatException()
    {
        // 4×2 image, all pixels transparent (index 0)
        var transparent = new HashSet<(int, int)>();
        for (int y = 0; y < 2; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                transparent.Add((x, y));
            }
        }

        var timBytes = BuildTim4Bit(widthWords: 1, height: 2, transparent);
        var tim = Dw3.Formats.Binary.Tim.TimDecoder.Decode(timBytes);

        Assert.Throws<FormatException>(() => PlayerArchiveDecoder.ComputeVisibleBbox(tim));
    }

    [Fact]
    public void ComputeVisibleBbox_VisibleSizeMatchesPythonFormula()
    {
        // 4×4 image: opaque pixels at x=1..2, y=1..2 only (2×2 region).
        // Python formula: visible_size = [right_excl - left + 1, lower_excl - upper + 1]
        //   = [3 - 1 + 1, 3 - 1 + 1] = [3, 3]  (off-by-one vs actual 2×2)
        var transparent = new HashSet<(int, int)>();
        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                if (!(x >= 1 && x <= 2 && y >= 1 && y <= 2))
                {
                    transparent.Add((x, y));
                }
            }
        }

        var timBytes = BuildTim4Bit(widthWords: 1, height: 4, transparent);
        var tim = Dw3.Formats.Binary.Tim.TimDecoder.Decode(timBytes);

        var bbox = PlayerArchiveDecoder.ComputeVisibleBbox(tim);

        // left=1, upper=1, right_excl=3 (maxX+1=2+1), lower_excl=3 (maxY+1=2+1)
        Assert.Equal(1, bbox.Left);
        Assert.Equal(1, bbox.Upper);
        Assert.Equal(3, bbox.RightExclusive);
        Assert.Equal(3, bbox.LowerExclusive);

        // Python visible_size formula:
        Assert.Equal(3, bbox.RightExclusive - bbox.Left + 1);  // 3 (off-by-one from actual 2)
        Assert.Equal(3, bbox.LowerExclusive - bbox.Upper + 1); // 3 (off-by-one from actual 2)
    }

    // ── DecodeAnimSetTable ─────────────────────────────────────────────────

    /// <summary>
    /// Builds a synthetic archive containing only an animation-set table
    /// at entry <paramref name="tableEntry"/>. The table has one set.
    /// </summary>
    private static byte[] BuildArchiveWithAnimSetTable(int tableEntry, ushort setId, ushort[] seqIndices)
    {
        // We need (tableEntry+1) offset entries + the payload
        // The table is at entry tableEntry; entries 0..(tableEntry-1) point to 4-byte gaps.
        // The set table payload: [setId][seq0][seq1][seq2][seq3][seq4][0] = 7 u16 = 14 bytes
        int offsetTableBytes = (tableEntry + 1) * 4;
        int priorPayloadBytes = tableEntry * 4; // each prior entry is 4 bytes
        int setTableOffset = offsetTableBytes + priorPayloadBytes;
        int setTableBytes = 7 * 2; // one group (6 u16) + terminator (1 u16)
        int totalSize = setTableOffset + setTableBytes;

        var data = new byte[totalSize];

        // Populate offset table for prior entries
        for (int i = 0; i < tableEntry; i++)
        {
            uint offset = (uint)(offsetTableBytes + (i * 4));
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(i * 4, 4), offset);
        }

        // Entry tableEntry → set table offset
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(tableEntry * 4, 4), (uint)setTableOffset);

        // Write the animation-set table
        int pos = setTableOffset;
        WriteU16LE(data, pos, setId);
        pos += 2;
        foreach (var idx in seqIndices)
        {
            WriteU16LE(data, pos, idx);
            pos += 2;
        }

        WriteU16LE(data, pos, 0); // terminator
        return data;
    }

    [Fact]
    public void DecodeAnimSetTable_OneSet_ReturnsCorrectGroup()
    {
        ushort[] seqIndices = [10, 11, 12, 13, 14];
        var data = BuildArchiveWithAnimSetTable(tableEntry: 3, setId: 1, seqIndices);
        var offsets = PlayerArchiveDecoder.DecodeOffsetTable(data, 3);

        var groups = PlayerArchiveDecoder.DecodeAnimSetTable(data, offsets, tableEntry: 3);

        Assert.Single(groups);
        Assert.Equal(1, groups[0].SetId);
        Assert.Equal(seqIndices.Select(x => (int)x).ToList(), [.. groups[0].SequenceIndices]);
    }

    [Fact]
    public void DecodeAnimSetTable_TruncatedBeforeFullGroup_ThrowsFormatException()
    {
        // Build archive whose set-table payload is too short: only 5 u16 (need 6 for one set + terminator)
        int tableEntry = 2;
        int offsetTableBytes = (tableEntry + 1) * 4;
        int priorPayloadBytes = tableEntry * 4;
        int setTableOffset = offsetTableBytes + priorPayloadBytes;

        // Only 5 u16 = 10 bytes (< 6+1=7 u16 needed)
        int totalSize = setTableOffset + (5 * 2);
        var data = new byte[totalSize];
        for (int i = 0; i < tableEntry; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(i * 4, 4), (uint)(offsetTableBytes + (i * 4)));
        }

        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(tableEntry * 4, 4), (uint)setTableOffset);
        WriteU16LE(data, setTableOffset, 5); // non-zero set id → must have 5 more u16 but only 4 remain

        Assert.Throws<FormatException>(() =>
        {
            var offsets = PlayerArchiveDecoder.DecodeOffsetTable(data, tableEntry);
            PlayerArchiveDecoder.DecodeAnimSetTable(data, offsets, tableEntry);
        });
    }

    [Fact]
    public void DecodeAnimSetTable_MissingTerminator_ThrowsFormatException()
    {
        // Build an archive where the set-table data ends exactly after one full set with no terminator
        int tableEntry = 2;
        int offsetTableBytes = (tableEntry + 1) * 4;
        int priorPayloadBytes = tableEntry * 4;
        int setTableOffset = offsetTableBytes + priorPayloadBytes;

        // One set = 6 u16, no terminator (6 u16 total, expected 7 for proper termination)
        int totalSize = setTableOffset + (6 * 2);
        var data = new byte[totalSize];
        for (int i = 0; i < tableEntry; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(i * 4, 4), (uint)(offsetTableBytes + (i * 4)));
        }

        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(tableEntry * 4, 4), (uint)setTableOffset);
        WriteU16LE(data, setTableOffset, 1); // set id = 1

        // seq indices 0..4 all zero (which equals the terminator ambiguity, but we want exactly 6 u16 with no 0 terminator after the set)
        WriteU16LE(data, setTableOffset + 2, 5);  // seq[0]
        WriteU16LE(data, setTableOffset + 4, 6);  // seq[1]
        WriteU16LE(data, setTableOffset + 6, 7);  // seq[2]
        WriteU16LE(data, setTableOffset + 8, 8);  // seq[3]
        WriteU16LE(data, setTableOffset + 10, 9); // seq[4]

        // No terminator u16 follows; the data simply ends.
        Assert.Throws<FormatException>(() =>
        {
            var offsets = PlayerArchiveDecoder.DecodeOffsetTable(data, tableEntry);
            PlayerArchiveDecoder.DecodeAnimSetTable(data, offsets, tableEntry);
        });
    }

    // ── DecodeSequence ─────────────────────────────────────────────────────

    /// <summary>
    /// Builds a synthetic archive with two adjacent sequence entries.
    /// Entry <paramref name="seqIndex"/> contains the given records followed by a terminator.
    /// Entry <paramref name="seqIndex"/>+1 is a single-word filler so the length is determined.
    /// </summary>
    private static byte[] BuildArchiveWithSequence(
        int seqIndex,
        (int Duration, int Frame, int OffsetX, int OffsetY)[] records,
        int terminator)
    {
        // Entries 0..(seqIndex-1) are 4-byte dummies.
        int entryCount = seqIndex + 2; // we need entries seqIndex and seqIndex+1
        int offsetTableBytes = entryCount * 4;
        int priorPayloadBytes = seqIndex * 4;
        int seqOffset = offsetTableBytes + priorPayloadBytes;

        // Sequence payload: N records (each 4 i32) + 1 terminator (1 i32) = (4N+1)*4 bytes
        int seqBytes = ((records.Length * 4) + 1) * 4;

        // Next entry padding: 4 bytes
        int nextOffset = seqOffset + seqBytes;
        int totalSize = nextOffset + 4;

        var data = new byte[totalSize];

        // Fill offset table
        for (int i = 0; i < seqIndex; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(i * 4, 4), (uint)(offsetTableBytes + (i * 4)));
        }

        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(seqIndex * 4, 4), (uint)seqOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan((seqIndex + 1) * 4, 4), (uint)nextOffset);

        // Write sequence records
        int pos = seqOffset;
        foreach (var (duration, frame, ox, oy) in records)
        {
            WriteI32LE(data, pos, duration);
            pos += 4;
            WriteI32LE(data, pos, frame);
            pos += 4;
            WriteI32LE(data, pos, ox);
            pos += 4;
            WriteI32LE(data, pos, oy);
            pos += 4;
        }

        WriteI32LE(data, pos, terminator);

        return data;
    }

    [Fact]
    public void DecodeSequence_LoopTerminator_ReturnsCorrectRecords()
    {
        var records = new (int, int, int, int)[] { (5, 3, -16, -28), (7, 4, -16, -28) };
        var data = BuildArchiveWithSequence(seqIndex: 2, records, terminator: 0); // 0 = loop
        var offsets = PlayerArchiveDecoder.DecodeOffsetTable(data, 3);

        var seq = PlayerArchiveDecoder.DecodeSequence(data, offsets, index: 2, frameCount: 10);

        Assert.Equal(PlayerAnimSequenceTerminator.Loop, seq.Terminator);
        Assert.Equal(2, seq.Records.Count);
        Assert.Equal(5, seq.Records[0].Duration);
        Assert.Equal(3, seq.Records[0].Frame);
        Assert.Equal(-16, seq.Records[0].OffsetX);
        Assert.Equal(-28, seq.Records[0].OffsetY);
    }

    [Fact]
    public void DecodeSequence_EndTerminator_ReturnsCorrectTerminator()
    {
        var records = new (int, int, int, int)[] { (3, 1, 0, 0) };
        var data = BuildArchiveWithSequence(seqIndex: 1, records, terminator: -1); // -1 = end
        var offsets = PlayerArchiveDecoder.DecodeOffsetTable(data, 2);

        var seq = PlayerArchiveDecoder.DecodeSequence(data, offsets, index: 1, frameCount: 10);

        Assert.Equal(PlayerAnimSequenceTerminator.End, seq.Terminator);
        Assert.Single(seq.Records);
    }

    [Fact]
    public void DecodeSequence_MissingTerminator_ThrowsFormatException()
    {
        // Build a sequence payload that has records but no terminator word at the end
        // We force this by making the next-entry offset land exactly on the last record word.
        int seqIndex = 1;
        int offsetTableBytes = (seqIndex + 2) * 4;
        int priorPayloadBytes = seqIndex * 4;
        int seqOffset = offsetTableBytes + priorPayloadBytes;

        // One record (4 i32) = 16 bytes, no terminator i32 following
        int seqBytes = 16;
        int nextOffset = seqOffset + seqBytes;
        int totalSize = nextOffset + 4;
        var data = new byte[totalSize];
        for (int i = 0; i < seqIndex; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(i * 4, 4), (uint)(offsetTableBytes + (i * 4)));
        }

        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(seqIndex * 4, 4), (uint)seqOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan((seqIndex + 1) * 4, 4), (uint)nextOffset);

        // Write one record with valid duration=5, frame=2, ox=0, oy=0
        int pos = seqOffset;
        WriteI32LE(data, pos, 5);
        pos += 4;
        WriteI32LE(data, pos, 2);
        pos += 4;
        WriteI32LE(data, pos, 0);
        pos += 4;
        WriteI32LE(data, pos, 0);

        // No terminator: the next entry starts here
        var offsets = PlayerArchiveDecoder.DecodeOffsetTable(data, seqIndex + 1);
        Assert.Throws<FormatException>(() =>
            PlayerArchiveDecoder.DecodeSequence(data, offsets, index: seqIndex, frameCount: 10));
    }

    [Fact]
    public void DecodeSequence_InvalidFrameIndex_ThrowsFormatException()
    {
        // Record with frame >= frameCount should raise a FormatException
        var records = new (int, int, int, int)[] { (5, 99, 0, 0) }; // frame=99, frameCount=10
        var data = BuildArchiveWithSequence(seqIndex: 0, records, terminator: 0);
        var offsets = PlayerArchiveDecoder.DecodeOffsetTable(data, 1);

        Assert.Throws<FormatException>(() =>
            PlayerArchiveDecoder.DecodeSequence(data, offsets, index: 0, frameCount: 10));
    }

    [Fact]
    public void DecodeSequence_ZeroLengthSequence_EmptyRecordsLoopTerminator()
    {
        // A sequence that immediately has a loop terminator (0) with no records is valid.
        var data = BuildArchiveWithSequence(seqIndex: 0, records: [], terminator: 0);
        var offsets = PlayerArchiveDecoder.DecodeOffsetTable(data, 1);

        var seq = PlayerArchiveDecoder.DecodeSequence(data, offsets, index: 0, frameCount: 10);

        Assert.Equal(PlayerAnimSequenceTerminator.Loop, seq.Terminator);
        Assert.Empty(seq.Records);
    }
}
