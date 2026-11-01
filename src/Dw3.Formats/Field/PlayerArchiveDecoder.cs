using System.Buffers.Binary;
using Dw3.Formats.Binary.Tim;

namespace Dw3.Formats.Field;

/// <summary>
/// Result of a visible-bounding-box scan on a player TIM frame.
/// Uses PIL-compatible exclusive right/lower coordinates to match the
/// Python parity baseline exactly.
/// </summary>
public readonly record struct PlayerFrameBbox(int Left, int Upper, int RightExclusive, int LowerExclusive)
{
    /// <summary>
    /// Width using the Python visible_size formula:
    /// RightExclusive - Left + 1  (off-by-one vs the actual pixel count).
    /// </summary>
    public int VisibleSizeWidth => RightExclusive - Left + 1;

    /// <summary>
    /// Height using the Python visible_size formula:
    /// LowerExclusive - Upper + 1  (off-by-one vs the actual pixel count).
    /// </summary>
    public int VisibleSizeHeight => LowerExclusive - Upper + 1;
}

/// <summary>One entry in the player animation-set table.</summary>
public sealed class PlayerAnimSetGroup
{
    /// <summary>Animation set identifier (1=STAND, 4=WALK, 5=RUN, 6=STOP, 7=unnamed).</summary>
    public int SetId { get; }

    /// <summary>
    /// Five archive-entry indices (one per facing direction) pointing to
    /// animation sequences within the same offset table.
    /// </summary>
    public IReadOnlyList<int> SequenceIndices { get; }

    internal PlayerAnimSetGroup(int setId, IReadOnlyList<int> sequenceIndices)
    {
        SetId = setId;
        SequenceIndices = sequenceIndices;
    }
}

/// <summary>How an animation sequence ends.</summary>
public enum PlayerAnimSequenceTerminator
{
    /// <summary>Word 0 at end of sequence — loop back to start.</summary>
    Loop,

    /// <summary>Word -1 at end of sequence — play once and stop.</summary>
    End,
}

/// <summary>One record in an animation sequence (duration + frame + draw offset).</summary>
public sealed class PlayerAnimRecord
{
    /// <summary>Duration in game ticks (> 0).</summary>
    public int Duration { get; }

    /// <summary>Zero-based index into the frame table (< FrameCount).</summary>
    public int Frame { get; }

    /// <summary>Draw offset X in pixels (tile-relative, from FIELDSTG_drawActor).</summary>
    public int OffsetX { get; }

    /// <summary>Draw offset Y in pixels (tile-relative, from FIELDSTG_drawActor).</summary>
    public int OffsetY { get; }

    internal PlayerAnimRecord(int duration, int frame, int offsetX, int offsetY)
    {
        Duration = duration;
        Frame = frame;
        OffsetX = offsetX;
        OffsetY = offsetY;
    }
}

/// <summary>A decoded animation sequence.</summary>
public sealed class PlayerAnimSequence
{
    /// <summary>Frame records in play order.</summary>
    public IReadOnlyList<PlayerAnimRecord> Records { get; }

    /// <summary>Type of terminator word that ends the sequence.</summary>
    public PlayerAnimSequenceTerminator Terminator { get; }

    internal PlayerAnimSequence(IReadOnlyList<PlayerAnimRecord> records, PlayerAnimSequenceTerminator terminator)
    {
        Records = records;
        Terminator = terminator;
    }
}

/// <summary>
/// Decodes the player sprite archive (P002PLAY.BIN) from the EU disc.
///
/// File layout (P002PLAY.BIN, disc path AAA/DAT/FIELD/SPRT/P002PLAY.BIN):
/// <list type="bullet">
///   <item>Offset table: (PlayerAnimSetTableEntry + 1) u32 little-endian offsets.</item>
///   <item>Entries 0..0x56 (87): 4-bit TIM images (player frame sprites).</item>
///   <item>Entries 0x57..0x6F (25): animation sequences (5 sets × 5 directions).</item>
///   <item>Entry 0x70 (112): animation-set table (set records terminated by 0).</item>
/// </list>
///
/// All integer fields are little-endian.
/// </summary>
public static class PlayerArchiveDecoder
{
    /// <summary>Disc-relative ISO path of the player sprite archive.</summary>
    public const string PlayerArchiveDiscPath = "AAA/DAT/FIELD/SPRT/P002PLAY.BIN";

    /// <summary>Number of TIM frame entries (entries 0x00..0x56 inclusive).</summary>
    public const int PlayerFrameCount = 0x57; // 87

    /// <summary>Archive entry index of the animation-set table.</summary>
    public const int PlayerAnimSetTableEntry = 0x70; // 112

    /// <summary>
    /// Known animation-set names by set ID.
    /// Set IDs observed in the player archive overlay (sets 1, 4, 5, 6, 7).
    /// </summary>
    private static readonly IReadOnlyDictionary<int, string> SetNames =
        new Dictionary<int, string>
        {
            [1] = "STAND",
            [4] = "WALK",
            [5] = "RUN",
            [6] = "STOP",
            [7] = "UNNAMED_SET_7",
        };

    /// <summary>Returns the name of an animation set by ID, or "set_N" if unknown.</summary>
    public static string GetSetName(int setId) =>
        SetNames.TryGetValue(setId, out string? name) ? name : $"set_{setId}";

    // ── Offset table ────────────────────────────────────────────────────────

    /// <summary>
    /// Reads the archive offset table from <paramref name="data"/>.
    /// The table occupies the first (<paramref name="lastEntry"/> + 1) × 4 bytes.
    /// Each entry is a u32 little-endian offset to the start of that archive entry's payload.
    ///
    /// Validates:
    /// <list type="bullet">
    ///   <item>Table fits within <paramref name="data"/>.</item>
    ///   <item>Each offset is ≥ table_end and &lt; data.Length.</item>
    ///   <item>Offsets are strictly ascending (no overlaps or duplicates).</item>
    /// </list>
    /// </summary>
    /// <param name="data">Raw file bytes.</param>
    /// <param name="lastEntry">Index of the last entry to read (inclusive).</param>
    /// <returns>Array of <c>lastEntry + 1</c> absolute byte offsets.</returns>
    /// <exception cref="FormatException">Any structural violation.</exception>
    public static int[] DecodeOffsetTable(ReadOnlySpan<byte> data, int lastEntry)
    {
        int count = lastEntry + 1;
        int tableBytes = count * 4;
        if (tableBytes > data.Length)
            throw new FormatException(
                $"Player archive offset table requires {tableBytes} bytes but the file is only {data.Length} bytes.");

        var offsets = new int[count];
        for (int i = 0; i < count; i++)
            offsets[i] = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(i * 4, 4)));

        // Validate: each offset must be >= end of table and < data.Length
        for (int i = 0; i < count; i++)
        {
            if (offsets[i] < tableBytes || offsets[i] >= data.Length)
                throw new FormatException(
                    $"Player archive offset[{i}] = 0x{offsets[i]:X} is outside valid payload range [{tableBytes:X}, {data.Length:X}).");
        }

        // Validate: strictly ascending
        for (int i = 1; i < count; i++)
        {
            if (offsets[i] <= offsets[i - 1])
                throw new FormatException(
                    $"Player archive offsets are not strictly ascending at index {i}: offsets[{i-1}]=0x{offsets[i-1]:X}, offsets[{i}]=0x{offsets[i]:X}.");
        }

        return offsets;
    }

    // ── Visible bounding box ────────────────────────────────────────────────

    /// <summary>
    /// Computes the visible (non-transparent) bounding box of a player TIM frame.
    ///
    /// Transparency: alpha channel byte = 0.  Matches the Python PIL getbbox()
    /// semantics including the exclusive right/lower coordinates (right = maxX + 1,
    /// lower = maxY + 1), which is what the Python parity baseline stores.
    ///
    /// The visible_size values in the manifest use the Python formula:
    ///   width  = right_exclusive  - left  + 1
    ///   height = lower_exclusive  - upper + 1
    /// which is one more than the actual pixel count (an off-by-one in the
    /// original Python code that this port replicates for parity).
    /// </summary>
    /// <param name="image">Decoded TIM frame with RGBA pixel data.</param>
    /// <returns>Bounding box with exclusive right/lower edges.</returns>
    /// <exception cref="FormatException">The frame has no visible (non-transparent) pixels.</exception>
    public static PlayerFrameBbox ComputeVisibleBbox(TimImage image)
    {
        int width = image.Width;
        int height = image.Height;
        byte[] pixels = image.Pixels;

        int minX = int.MaxValue, minY = int.MaxValue;
        int maxX = int.MinValue, maxY = int.MinValue;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                // RGBA layout: R at [i*4], G at [i*4+1], B at [i*4+2], A at [i*4+3]
                byte alpha = pixels[(y * width + x) * 4 + 3];
                if (alpha > 0)
                {
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
        }

        if (minX == int.MaxValue)
            throw new FormatException($"Player frame {width}×{height} has no visible pixels.");

        // right = maxX + 1 (exclusive), lower = maxY + 1 (exclusive)
        // This matches PIL getbbox() output stored verbatim in the Python manifest.
        return new PlayerFrameBbox(minX, minY, maxX + 1, maxY + 1);
    }

    // ── Animation-set table ──────────────────────────────────────────────────

    /// <summary>
    /// Decodes the animation-set table from entry <paramref name="tableEntry"/> of
    /// the archive.
    ///
    /// Table layout (at offsets[tableEntry]):
    /// <code>
    ///   repeat:
    ///     u16  set_id      (non-zero: animation set identifier)
    ///     u16  seq_index_0 (archive offset-table index for direction 0)
    ///     u16  seq_index_1
    ///     u16  seq_index_2
    ///     u16  seq_index_3
    ///     u16  seq_index_4
    ///   u16  0x0000       (terminator)
    /// </code>
    /// </summary>
    /// <param name="data">Raw file bytes.</param>
    /// <param name="offsets">Decoded offset table (from DecodeOffsetTable).</param>
    /// <param name="tableEntry">Index of the animation-set table entry.</param>
    /// <returns>List of animation-set groups in disc order.</returns>
    /// <exception cref="FormatException">Structural violation (truncated or missing terminator).</exception>
    public static IReadOnlyList<PlayerAnimSetGroup> DecodeAnimSetTable(
        ReadOnlySpan<byte> data, int[] offsets, int tableEntry)
    {
        if (tableEntry < 0 || tableEntry >= offsets.Length)
            throw new FormatException(
                $"Animation-set table entry {tableEntry} is outside the offset table (length {offsets.Length}).");

        int start = offsets[tableEntry];
        // Read u16 words from start to end of data (table is not length-prefixed)
        if ((data.Length - start) % 2 != 0)
            throw new FormatException(
                "Player animation-set table payload has an odd number of bytes.");

        int wordCount = (data.Length - start) / 2;
        var groups = new List<PlayerAnimSetGroup>();
        int cursor = 0;

        while (cursor < wordCount)
        {
            ushort setId = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(start + cursor * 2, 2));
            if (setId == 0)
                return groups;

            // Need 5 more u16 for the sequence indices
            if (cursor + 6 > wordCount)
                throw new FormatException(
                    $"Player animation-set table entry at word offset {cursor} is truncated (set_id=0x{setId:X}, need 6 words).");

            var seqIndices = new int[5];
            for (int d = 0; d < 5; d++)
                seqIndices[d] = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(start + (cursor + 1 + d) * 2, 2));

            groups.Add(new PlayerAnimSetGroup(setId, seqIndices));
            cursor += 6;
        }

        throw new FormatException("Player animation-set table lacks the required 0x0000 terminator.");
    }

    // ── Animation sequence ───────────────────────────────────────────────────

    /// <summary>
    /// Decodes one animation sequence from the archive.
    ///
    /// Sequence layout at offsets[index]..offsets[index+1]:
    /// <code>
    ///   repeat:
    ///     i32  duration    (> 0: game-tick count for this frame)
    ///     i32  frame       (0-based TIM frame index)
    ///     i32  offset_x    (pixel draw offset X, tile-relative)
    ///     i32  offset_y    (pixel draw offset Y, tile-relative)
    ///   i32  0            (loop terminator) OR -1 (end terminator)
    /// </code>
    ///
    /// Each group of 4 i32 is one record; a single i32 = 0 or -1 ends the sequence.
    /// Any other non-positive duration is an error.
    /// </summary>
    /// <param name="data">Raw file bytes.</param>
    /// <param name="offsets">Decoded offset table.</param>
    /// <param name="index">Archive entry index of this sequence.</param>
    /// <param name="frameCount">Valid frame count; frame indices must be &lt; frameCount.</param>
    /// <returns>Decoded sequence with records and terminator type.</returns>
    /// <exception cref="FormatException">Structural violation.</exception>
    public static PlayerAnimSequence DecodeSequence(
        ReadOnlySpan<byte> data, int[] offsets, int index, int frameCount)
    {
        if (index < 0 || index >= offsets.Length)
            throw new FormatException(
                $"Sequence index {index} is outside the offset table (length {offsets.Length}).");

        int start = offsets[index];
        int end = index + 1 < offsets.Length ? offsets[index + 1] : data.Length;

        int byteLen = end - start;
        if (byteLen % 4 != 0)
            throw new FormatException(
                $"Sequence {index} payload is not 4-byte aligned ({byteLen} bytes).");

        int wordCount = byteLen / 4;
        var records = new List<PlayerAnimRecord>();
        int cursor = 0;

        while (cursor < wordCount)
        {
            int duration = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(start + cursor * 4, 4));

            if (duration == 0)
            {
                if (cursor != wordCount - 1)
                    throw new FormatException(
                        $"Sequence {index}: loop terminator (0) is not the final word (at cursor {cursor}, wordCount {wordCount}).");
                return new PlayerAnimSequence(records, PlayerAnimSequenceTerminator.Loop);
            }

            if (duration == -1)
            {
                if (cursor != wordCount - 1)
                    throw new FormatException(
                        $"Sequence {index}: end terminator (-1) is not the final word (at cursor {cursor}, wordCount {wordCount}).");
                return new PlayerAnimSequence(records, PlayerAnimSequenceTerminator.End);
            }

            if (duration <= 0)
                throw new FormatException(
                    $"Sequence {index} at cursor {cursor}: invalid duration {duration} (must be > 0, 0, or -1).");

            if (cursor + 4 > wordCount)
                throw new FormatException(
                    $"Sequence {index} at cursor {cursor}: truncated frame record (need 4 words, {wordCount - cursor} remain).");

            int frame   = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(start + (cursor + 1) * 4, 4));
            int offsetX = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(start + (cursor + 2) * 4, 4));
            int offsetY = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(start + (cursor + 3) * 4, 4));

            if (frame < 0 || frame >= frameCount)
                throw new FormatException(
                    $"Sequence {index} at cursor {cursor}: frame index {frame} is out of range [0, {frameCount}).");

            records.Add(new PlayerAnimRecord(duration, frame, offsetX, offsetY));
            cursor += 4;
        }

        throw new FormatException(
            $"Sequence {index}: payload ended without a terminator word (0 or -1).");
    }
}
