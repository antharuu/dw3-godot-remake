using System.Buffers.Binary;

namespace Dw3.Formats.Battle;

/// <summary>Decodes the key and motion archives referenced by a battle model descriptor.</summary>
public static class BattleAnimationDecoder
{
    public static BattleBoneTracks DecodeTracks(byte[] data, int start, int end, int bone)
    {
        int[] offsets = ReadOffsets(data, start, end, 3, $"bone {bone}: key track");
        var tracks = new IReadOnlyList<BattleTrackKey>[3];
        for (int track = 0; track < 3; track++)
        {
            int relative = offsets[track];
            if (relative == 0)
                throw new FormatException($"bone {bone}: key track {track} missing");
            int position = start + relative;
            int limit = offsets.Where(value => value > relative).Select(value => start + value).DefaultIfEmpty(end).Min();
            var keys = new List<BattleTrackKey>();
            int lastFrame = -1;
            while (position + 8 <= limit)
            {
                short x = ReadI16(data, position);
                short y = ReadI16(data, position + 2);
                short z = ReadI16(data, position + 4);
                short frame = ReadI16(data, position + 6);
                if (frame < lastFrame) break;
                keys.Add(new BattleTrackKey(frame, x, y, z));
                lastFrame = frame;
                position += 8;
            }
            if (keys.Count == 0)
                throw new FormatException($"bone {bone}: key track {track} has no keys");
            tracks[track] = keys;
        }
        return new BattleBoneTracks(tracks[0], tracks[1], tracks[2]);
    }

    public static IReadOnlyList<IReadOnlyList<BattleMotionStep>> DecodeMotions(byte[] data, int start, int end)
    {
        if (start < 0 || end > data.Length || start >= end)
            throw new FormatException("Invalid motion archive bounds.");
        var offsets = new List<int>();
        for (int index = 0; index < 64 && start + index * 4 + 4 <= end; index++)
        {
            int value = checked((int)ReadU32(data, start + index * 4));
            if (value == 0) continue;
            if (value < 4 * (index + 1) || value >= end - start) break;
            offsets.Add(value);
        }
        if (offsets.Count == 0)
            throw new FormatException("no motion offsets recovered");
        var motions = new List<IReadOnlyList<BattleMotionStep>>(offsets.Count);
        for (int index = 0; index < offsets.Count; index++)
        {
            int position = start + offsets[index];
            int limit = start + (index + 1 < offsets.Count ? offsets[index + 1] : end - start);
            var steps = new List<BattleMotionStep>();
            bool terminated = false;
            for (int count = 0; count < 256; count++, position += 8)
            {
                if (position + 8 > limit)
                    throw new FormatException($"motion {index + 1} overruns its region");
                var step = new BattleMotionStep(ReadI16(data, position), ReadI16(data, position + 2), ReadI16(data, position + 4), ReadI16(data, position + 6));
                steps.Add(step);
                if (step.Index == 0x7FFF) { terminated = true; break; }
            }
            if (!terminated)
                throw new FormatException($"motion {index + 1} missing terminator");
            motions.Add(steps);
        }
        return motions;
    }

    private static int[] ReadOffsets(byte[] data, int start, int end, int count, string label)
    {
        if (start < 0 || end > data.Length || start >= end || start + count * 4 > end)
            throw new FormatException($"Invalid {label} archive bounds.");
        var result = new int[count];
        for (int i = 0; i < count; i++)
        {
            uint raw = ReadU32(data, start + i * 4);
            if (raw > int.MaxValue || raw != 0 && (raw < count * 4 || raw >= end - start))
                throw new FormatException($"{label} {i} has invalid offset 0x{raw:X}");
            result[i] = (int)raw;
        }
        return result;
    }

    private static short ReadI16(byte[] data, int at) => BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(at, 2));
    private static uint ReadU32(byte[] data, int at) => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at, 4));
}
