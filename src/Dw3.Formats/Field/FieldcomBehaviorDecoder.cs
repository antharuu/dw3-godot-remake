namespace Dw3.Formats.Field;

/// <summary>One frame and duration pair recovered from field overlay data.</summary>
public sealed record FieldcomFrameStep(int Frame, int Duration);

/// <summary>A terminated frame sequence recovered from a field overlay.</summary>
public sealed record FieldcomFrameSequence(IReadOnlyList<FieldcomFrameStep> Steps);

/// <summary>An icon animation whose loop sentinel points to <see cref="LoopTarget"/>.</summary>
public sealed record FieldcomAnimation(IReadOnlyList<FieldcomFrameStep> Steps, int LoopTarget, bool HasTrailingPadding = false);

/// <summary>
/// Recovers FIELDCOM cue and indicator timing records from the supported field overlay.
/// The decoder only accepts fully terminated records and rejects ambiguous discoveries.
/// </summary>
public static class FieldcomBehaviorDecoder
{
    private const byte Terminator = 0xFF;
    private const byte CueDuration = 22;
    private const int CueRecordBytes = 9;
    private const int ExpectedCueSequences = 5;
    private const int ExpectedIconAnimations = 4;

    /// <exception cref="FormatException">The overlay lacks exactly the five terminated cue sequences.</exception>
    public static IReadOnlyList<FieldcomFrameSequence> DecodeCueInnerSequences(ReadOnlySpan<byte> overlay)
    {
        var sequences = new List<FieldcomFrameSequence>();
        for (int start = 0; start + CueRecordBytes <= overlay.Length; start++)
        {
            if (overlay[start + 1] != CueDuration || overlay[start + 3] != CueDuration || overlay[start + 4] != Terminator ||
                overlay[start + 5] != 0 || overlay[start + 6] != 0 || overlay[start + 7] != 0 || overlay[start + 8] != 0)
                continue;
            int first = overlay[start];
            int second = overlay[start + 2];
            if (first is not (>= 0x0C and <= 0x13 or >= 0x45 and <= 0x46) ||
                second is not (>= 0x0C and <= 0x13 or >= 0x45 and <= 0x46))
                continue;
            sequences.Add(new FieldcomFrameSequence([new FieldcomFrameStep(first, CueDuration), new FieldcomFrameStep(second, CueDuration)]));
        }
        if (sequences.Count != ExpectedCueSequences)
            throw new FormatException($"FIELDCOM cue data has {sequences.Count} terminated inner sequences, expected {ExpectedCueSequences}");
        return sequences.OrderBy(sequence => sequence.Steps[0].Frame).ToArray();
    }

    /// <exception cref="FormatException">The overlay lacks exactly four valid icon loops.</exception>
    public static IReadOnlyList<FieldcomAnimation> DecodeIconAnimations(ReadOnlySpan<byte> overlay, IReadOnlySet<int> frameIds)
    {
        ArgumentNullException.ThrowIfNull(frameIds);
        int lastCue = FindLastCueRecord(overlay);
        if (lastCue < 0)
            throw new FormatException("FIELDCOM icon data cannot locate the preceding cue sequence block");
        int searchStart = lastCue + CueRecordBytes;
        int searchEnd = Math.Min(overlay.Length - 4, searchStart + 0x100);
        var animations = new List<FieldcomAnimation>();
        for (int start = searchStart; start <= searchEnd; start++)
        {
            if (start >= 2 && frameIds.Contains(overlay[start - 2]) && overlay[start - 1] != 0)
                continue;
            var steps = new List<FieldcomFrameStep>();
            int cursor = start;
            while (cursor + 1 < overlay.Length && steps.Count < 64)
            {
                int frame = overlay[cursor];
                int duration = overlay[cursor + 1];
                if (frame == Terminator)
                {
                    if (steps.Count >= 2 && duration >= 0 && duration < steps.Count && frameIds.Contains(steps[duration].Frame))
                    {
                        bool hasTrailingPadding = cursor + 3 < overlay.Length && overlay[cursor + 2] == 0 && overlay[cursor + 3] == 0;
                        animations.Add(new FieldcomAnimation(steps, duration, hasTrailingPadding));
                    }
                    break;
                }
                if (!frameIds.Contains(frame) || duration <= 0)
                    break;
                steps.Add(new FieldcomFrameStep(frame, duration));
                cursor += 2;
            }
        }
        if (animations.Count != ExpectedIconAnimations)
            throw new FormatException($"FIELDCOM icon data has {animations.Count} valid animation loops, expected {ExpectedIconAnimations}");
        return animations;
    }

    private static int FindLastCueRecord(ReadOnlySpan<byte> overlay)
    {
        int last = -1;
        for (int start = 0; start + CueRecordBytes <= overlay.Length; start++)
            if (overlay[start] is (>= 0x0C and <= 0x13 or >= 0x45 and <= 0x46) && overlay[start + 2] is (>= 0x0C and <= 0x13 or >= 0x45 and <= 0x46) &&
                overlay[start + 1] == CueDuration && overlay[start + 3] == CueDuration && overlay[start + 4] == Terminator &&
                overlay[start + 5] == 0 && overlay[start + 6] == 0 && overlay[start + 7] == 0 && overlay[start + 8] == 0)
                last = start;
        return last;
    }
}
