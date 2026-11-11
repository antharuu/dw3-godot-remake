using System.Buffers.Binary;

namespace Dw3.Formats.Battle;

/// <summary>Decodes the key and motion archives referenced by a battle model descriptor.</summary>
public static class BattleAnimationDecoder
{
    private const int KeyBytes = 8;
    private const int StepBytes = 8;
    private const int MaximumMotions = 64;
    private const int MaximumSteps = 256;
    private const short StepTerminator = 0x7FFF;

    /// <summary>
    /// Decodes the three keyframe tracks (translation, rotation, scale) for one bone from the
    /// model's key archive. Each track is an offset into the archive that points to a list of
    /// 8-byte keys (s16 x, y, z, frame), terminated when the frame number goes backwards.
    /// </summary>
    /// <param name="data">Full model file bytes.</param>
    /// <param name="start">File offset of the bone's track offset table (3 × u32).</param>
    /// <param name="end">File offset of the end of the bone's key region.</param>
    /// <param name="bone">Zero-based bone index, used only in error messages.</param>
    /// <returns>The decoded translation, rotation and scale tracks for this bone.</returns>
    /// <exception cref="FormatException">An offset is invalid, a track is empty, or the archive bounds are wrong.</exception>
    public static BattleBoneTracks DecodeTracks(byte[] data, int start, int end, int bone)
    {
        int[] offsets = ReadOffsets(data, start, end, 3, $"bone {bone}: key track");
        var tracks = new IReadOnlyList<BattleTrackKey>[3];
        for (int track = 0; track < 3; track++)
        {
            int relative = offsets[track];
            if (relative == 0)
            {
                throw new FormatException($"bone {bone}: key track {track} missing");
            }

            int limit = offsets.Where(value => value > relative).Select(value => start + value).DefaultIfEmpty(end).Min();
            List<BattleTrackKey> keys = ReadTrackKeys(data, start + relative, limit);
            if (keys.Count == 0)
            {
                throw new FormatException($"bone {bone}: key track {track} has no keys");
            }

            tracks[track] = keys;
        }

        return new BattleBoneTracks(tracks[0], tracks[1], tracks[2]);
    }

    /// <summary>
    /// Decodes all motion sequences from a motion archive. The archive starts with a table of up to
    /// 64 u32 offsets; each non-zero offset points to a sequence of 8-byte steps (s16 index, count,
    /// frame, endFrame) terminated by a step whose index is 0x7FFF.
    /// </summary>
    /// <param name="data">Full motion file bytes.</param>
    /// <param name="start">File offset of the motion archive's offset table.</param>
    /// <param name="end">File offset of the end of the motion archive.</param>
    /// <returns>One entry per recovered motion, each a list of steps including the 0x7FFF terminator.</returns>
    /// <exception cref="FormatException">The archive bounds are invalid, no offsets are found, or a motion is truncated or unterminated.</exception>
    public static IReadOnlyList<IReadOnlyList<BattleMotionStep>> DecodeMotions(byte[] data, int start, int end)
    {
        if (start < 0 || end > data.Length || start >= end)
        {
            throw new FormatException("Invalid motion archive bounds.");
        }

        List<int> offsets = ReadMotionOffsets(data, start, end);
        if (offsets.Count == 0)
        {
            throw new FormatException("no motion offsets recovered");
        }

        var motions = new List<IReadOnlyList<BattleMotionStep>>(offsets.Count);
        for (int index = 0; index < offsets.Count; index++)
        {
            int relative = offsets[index];
            int limit = offsets.Where(value => value > relative).Select(value => start + value).DefaultIfEmpty(end).Min();
            motions.Add(relative == 0 ? [] : ReadMotion(data, start + relative, limit, index + 1));
        }

        return motions;
    }

    /// <summary>
    /// Reads 8-byte keys { s16 x, y, z, frame } from <paramref name="position"/> until
    /// <paramref name="limit"/> or until a frame number goes backwards.
    /// </summary>
    /// <param name="data">Whole model file.</param>
    /// <param name="position">File offset of the first key.</param>
    /// <param name="limit">File offset where the track's region ends.</param>
    /// <returns>The keys in file order, possibly empty.</returns>
    private static List<BattleTrackKey> ReadTrackKeys(byte[] data, int position, int limit)
    {
        var keys = new List<BattleTrackKey>();
        int lastFrame = -1;
        while (position + KeyBytes <= limit)
        {
            short x = ReadI16(data, position);
            short y = ReadI16(data, position + 2);
            short z = ReadI16(data, position + 4);
            short frame = ReadI16(data, position + 6);
            if (frame < lastFrame)
            {
                break;
            }

            keys.Add(new BattleTrackKey(frame, x, y, z));
            lastFrame = frame;
            position += KeyBytes;
        }

        return keys;
    }

    /// <summary>
    /// Reads the motion offset table: up to 64 u32 offsets relative to <paramref name="start"/>.
    /// Zero slots are skipped; the first offset that points into the table or past the archive ends it.
    /// </summary>
    /// <param name="data">Whole motion file.</param>
    /// <param name="start">File offset of the motion archive.</param>
    /// <param name="end">File offset where the motion archive ends.</param>
    /// <returns>The recovered offsets in table order, possibly empty.</returns>
    /// <exception cref="OverflowException">An offset exceeds <see cref="int.MaxValue"/>.</exception>
    private static List<int> ReadMotionOffsets(byte[] data, int start, int end)
    {
        var offsets = new List<int>();
        int tableEnd = end - start;
        for (int index = 0; index < MaximumMotions && (index + 1) * 4 <= tableEnd; index++)
        {
            int value = checked((int)ReadU32(data, start + (index * 4)));
            if (value != 0 && (value < 4 * (index + 1) || value >= end - start))
            {
                break;
            }

            if (value != 0)
            {
                tableEnd = Math.Min(tableEnd, value);
            }

            offsets.Add(value);
        }

        while (offsets.Count > 0 && offsets[^1] == 0)
        {
            offsets.RemoveAt(offsets.Count - 1);
        }

        return offsets;
    }

    /// <summary>Reads the 8-byte steps of one motion up to and including its 0x7FFF terminator step.</summary>
    /// <param name="data">Whole motion file.</param>
    /// <param name="position">File offset of the first step.</param>
    /// <param name="limit">File offset where the motion's region ends.</param>
    /// <param name="number">One-based motion number, used in error messages.</param>
    /// <returns>The steps, terminator included.</returns>
    /// <exception cref="FormatException">A step overruns the region or no terminator appears within 256 steps.</exception>
    private static List<BattleMotionStep> ReadMotion(byte[] data, int position, int limit, int number)
    {
        var steps = new List<BattleMotionStep>();
        for (int count = 0; count < MaximumSteps; count++, position += StepBytes)
        {
            if (position + StepBytes > limit)
            {
                throw new FormatException($"motion {number} overruns its region");
            }

            var step = new BattleMotionStep(ReadI16(data, position), ReadI16(data, position + 2), ReadI16(data, position + 4), ReadI16(data, position + 6));
            steps.Add(step);
            if (step.Index == StepTerminator)
            {
                return steps;
            }
        }

        throw new FormatException($"motion {number} missing terminator");
    }

    /// <summary>
    /// Reads a fixed-count table of u32 offsets relative to <paramref name="start"/>; validates
    /// bounds and range before returning, so callers can dereference offsets safely.
    /// </summary>
    /// <param name="data">Full file bytes.</param>
    /// <param name="start">File offset of the offset table.</param>
    /// <param name="end">File offset of the end of the covered archive.</param>
    /// <param name="count">Number of u32 slots to read.</param>
    /// <param name="label">Human-readable label used in error messages.</param>
    /// <returns>The decoded offsets as signed integers, already validated against the archive bounds.</returns>
    /// <exception cref="FormatException">The archive bounds are invalid or an offset is out of range.</exception>
    private static int[] ReadOffsets(byte[] data, int start, int end, int count, string label)
    {
        if (start < 0 || end > data.Length || start >= end || start + (count * 4) > end)
        {
            throw new FormatException($"Invalid {label} archive bounds.");
        }

        var result = new int[count];
        for (int i = 0; i < count; i++)
        {
            uint raw = ReadU32(data, start + (i * 4));
            if (raw > int.MaxValue || (raw != 0 && (raw < count * 4 || raw >= end - start)))
            {
                throw new FormatException($"{label} {i} has invalid offset 0x{raw:X}");
            }

            result[i] = (int)raw;
        }

        return result;
    }

    /// <summary>Reads a little-endian signed 16-bit integer from <paramref name="data"/> at byte offset <paramref name="at"/>.</summary>
    /// <param name="data">Source byte array.</param>
    /// <param name="at">Byte offset of the first byte; the read covers exactly 2 bytes.</param>
    /// <returns>The decoded s16 value.</returns>
    private static short ReadI16(byte[] data, int at) => BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(at, 2));

    /// <summary>Reads a little-endian unsigned 32-bit integer from <paramref name="data"/> at byte offset <paramref name="at"/>.</summary>
    /// <param name="data">Source byte array.</param>
    /// <param name="at">Byte offset of the first byte; the read covers exactly 4 bytes.</param>
    /// <returns>The decoded u32 value.</returns>
    private static uint ReadU32(byte[] data, int at) => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at, 4));
}
