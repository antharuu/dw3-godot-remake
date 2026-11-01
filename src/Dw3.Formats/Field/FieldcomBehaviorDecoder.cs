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

    /// <summary>Returns true when the byte sequence at start matches a valid two-step cue record.</summary>
    /// <param name="overlay">Raw overlay bytes.</param>
    /// <param name="start">Start offset of the candidate nine-byte record.</param>
    /// <param name="sequence">Receives the decoded frame sequence on success.</param>
    /// <returns>True when the record matches and sequence is populated.</returns>
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

    /// <summary>Returns true when frame is within the valid cue animation range.</summary>
    /// <param name="frame">Animation frame index to test.</param>
    /// <returns>True when frame is in [0x0C, 0x13] or [0x45, 0x46].</returns>
    private static bool IsValidCueFrame(int frame) =>
        frame is (>= 0x0C and <= 0x13 or >= 0x45 and <= 0x46);

    /// <summary>Returns true when start is immediately preceded by a frame that blocks a new animation start.</summary>
    /// <param name="overlay">Raw overlay bytes.</param>
    /// <param name="start">Candidate start offset to test.</param>
    /// <param name="frameIds">Valid frame identifier set.</param>
    /// <returns>True when the two bytes before start form a valid (frame, duration) step that prevents a new start.</returns>
    private static bool IsBlockedByPrecedingFrame(ReadOnlySpan<byte> overlay, int start, IReadOnlySet<int> frameIds) =>
        start >= 2 && frameIds.Contains(overlay[start - 2]) && overlay[start - 1] != 0;

    /// <summary>Attempts to build an icon animation starting at the given byte offset.</summary>
    /// <param name="overlay">Raw overlay bytes.</param>
    /// <param name="start">Candidate byte offset for the first step of the animation.</param>
    /// <param name="frameIds">Valid frame identifier set for step validation.</param>
    /// <param name="animation">Receives the completed animation on success.</param>
    /// <returns>True when a valid terminated animation was built; false otherwise.</returns>
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

    /// <summary>Validates the terminator and builds a complete animation when the loop target is valid.</summary>
    /// <param name="overlay">Raw overlay bytes (used to check trailing padding).</param>
    /// <param name="cursor">Byte offset of the terminator byte.</param>
    /// <param name="steps">Collected frame steps before the terminator.</param>
    /// <param name="loopTarget">Loop-back target step index from the terminator byte.</param>
    /// <param name="frameIds">Valid frame identifier set.</param>
    /// <param name="animation">Receives the completed animation on success.</param>
    /// <returns>True when the loop target is valid and the animation was built.</returns>
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

    /// <summary>Returns the overlay offset of the last valid cue record, or -1 when none found.</summary>
    /// <param name="overlay">Raw overlay bytes to scan.</param>
    /// <returns>Byte offset of the last matching nine-byte cue record, or -1.</returns>
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

    /// <summary>Returns true when the nine bytes starting at start form a complete cue record.</summary>
    /// <param name="overlay">Raw overlay bytes.</param>
    /// <param name="start">Start offset of the nine-byte candidate.</param>
    /// <returns>True when frame, duration and padding bytes match the cue record layout.</returns>
    private static bool IsValidCueRecord(ReadOnlySpan<byte> overlay, int start) =>
        IsValidCueFrame(overlay[start]) && IsValidCueFrame(overlay[start + 2]) &&
        overlay[start + 1] == CueDuration && overlay[start + 3] == CueDuration && overlay[start + 4] == Terminator &&
        overlay[start + 5] == 0 && overlay[start + 6] == 0 && overlay[start + 7] == 0 && overlay[start + 8] == 0;
}
