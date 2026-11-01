namespace Dw3.Formats.Field;

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
            if (TryParseCueRecord(overlay, start, out FieldcomFrameSequence sequence))
            {
                sequences.Add(sequence);
            }
        }

        if (sequences.Count != ExpectedCueSequences)
        {
            throw new FormatException($"FIELDCOM cue data has {sequences.Count} terminated inner sequences, expected {ExpectedCueSequences}");
        }

        return [.. sequences.OrderBy(sequence => sequence.Steps[0].Frame)];
    }

    /// <exception cref="FormatException">The overlay lacks exactly four valid icon loops.</exception>
    public static IReadOnlyList<FieldcomAnimation> DecodeIconAnimations(ReadOnlySpan<byte> overlay, IReadOnlySet<int> frameIds)
    {
        ArgumentNullException.ThrowIfNull(frameIds);
        int lastCue = FindLastCueRecord(overlay);
        if (lastCue < 0)
        {
            throw new FormatException("FIELDCOM icon data cannot locate the preceding cue sequence block");
        }

        int searchStart = lastCue + CueRecordBytes;
        int searchEnd = Math.Min(overlay.Length - 4, searchStart + 0x100);
        var animations = new List<FieldcomAnimation>();
        for (int start = searchStart; start <= searchEnd; start++)
        {
            if (IsBlockedByPrecedingFrame(overlay, start, frameIds))
            {
                continue;
            }

            if (TryBuildAnimation(overlay, start, frameIds, out FieldcomAnimation animation))
            {
                animations.Add(animation);
            }
        }

        if (animations.Count != ExpectedIconAnimations)
        {
            throw new FormatException($"FIELDCOM icon data has {animations.Count} valid animation loops, expected {ExpectedIconAnimations}");
        }

        return animations;
    }

    private static bool TryParseCueRecord(ReadOnlySpan<byte> overlay, int start, out FieldcomFrameSequence sequence)
    {
        sequence = null!;
        if (overlay[start + 1] != CueDuration || overlay[start + 3] != CueDuration || overlay[start + 4] != Terminator ||
            overlay[start + 5] != 0 || overlay[start + 6] != 0 || overlay[start + 7] != 0 || overlay[start + 8] != 0)
        {
            return false;
        }

        int first = overlay[start];
        int second = overlay[start + 2];
        if (!IsValidCueFrame(first) || !IsValidCueFrame(second))
        {
            return false;
        }

        sequence = new FieldcomFrameSequence([new FieldcomFrameStep(first, CueDuration), new FieldcomFrameStep(second, CueDuration)]);
        return true;
    }

    private static bool IsValidCueFrame(int frame) =>
        frame is (>= 0x0C and <= 0x13 or >= 0x45 and <= 0x46);

    private static bool IsBlockedByPrecedingFrame(ReadOnlySpan<byte> overlay, int start, IReadOnlySet<int> frameIds) =>
        start >= 2 && frameIds.Contains(overlay[start - 2]) && overlay[start - 1] != 0;

    private static bool TryBuildAnimation(ReadOnlySpan<byte> overlay, int start, IReadOnlySet<int> frameIds, out FieldcomAnimation animation)
    {
        animation = null!;
        var steps = new List<FieldcomFrameStep>();
        int cursor = start;
        while (cursor + 1 < overlay.Length && steps.Count < 64)
        {
            int frame = overlay[cursor];
            int duration = overlay[cursor + 1];
            if (frame == Terminator)
            {
                return TryCompleteAnimation(overlay, cursor, steps, duration, frameIds, out animation);
            }

            if (!frameIds.Contains(frame) || duration <= 0)
            {
                break;
            }

            steps.Add(new FieldcomFrameStep(frame, duration));
            cursor += 2;
        }

        return false;
    }

    private static bool TryCompleteAnimation(ReadOnlySpan<byte> overlay, int cursor, List<FieldcomFrameStep> steps, int loopTarget, IReadOnlySet<int> frameIds, out FieldcomAnimation animation)
    {
        animation = null!;
        if (steps.Count < 2 || loopTarget < 0 || loopTarget >= steps.Count || !frameIds.Contains(steps[loopTarget].Frame))
        {
            return false;
        }

        bool hasTrailingPadding = cursor + 3 < overlay.Length && overlay[cursor + 2] == 0 && overlay[cursor + 3] == 0;
        animation = new FieldcomAnimation(steps, loopTarget, hasTrailingPadding);
        return true;
    }

    private static int FindLastCueRecord(ReadOnlySpan<byte> overlay)
    {
        int last = -1;
        for (int start = 0; start + CueRecordBytes <= overlay.Length; start++)
        {
            if (IsValidCueRecord(overlay, start))
            {
                last = start;
            }
        }

        return last;
    }

    private static bool IsValidCueRecord(ReadOnlySpan<byte> overlay, int start) =>
        IsValidCueFrame(overlay[start]) && IsValidCueFrame(overlay[start + 2]) &&
        overlay[start + 1] == CueDuration && overlay[start + 3] == CueDuration && overlay[start + 4] == Terminator &&
        overlay[start + 5] == 0 && overlay[start + 6] == 0 && overlay[start + 7] == 0 && overlay[start + 8] == 0;
}
