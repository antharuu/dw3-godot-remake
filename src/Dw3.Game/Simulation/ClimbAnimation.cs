using Dw3Production.Conversion;

namespace Dw3Production.Simulation;

/// <summary>
/// Samples a climb animation frame from the decoded climb archive, using the same
/// source-duration cadence as PlayerAnimation. One-shot (ending) sequences hold their
/// last frame after they complete; looping sequences cycle forever. All five directions
/// of a climb set resolve to the same sequence, so only the source direction's flip is
/// applied at draw time (dir &gt;= 5 mirrors, exactly like the player sprites).
/// </summary>
public static class ClimbAnimation
{
    /// <summary>
    /// Returns the animation sample for the given climb set and accumulated tick count.
    /// Throws when the set is absent, the sequence has no records, or the total duration is zero.
    /// </summary>
    /// <param name="data">Decoded climb animation data from the disc archive.</param>
    /// <param name="set">Animation set index (e.g. 0x1A for ClimbUp, 0x20 for ClimbIdle).</param>
    /// <param name="ticks">Accumulated simulation ticks since the current animation started.</param>
    /// <returns>The resolved <see cref="ClimbAnimationSample"/> for the current tick.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the set is absent, the sequence is empty, or the total duration is not positive.</exception>
    public static ClimbAnimationSample Sample(ClimbData data, int set, int ticks)
    {
        if (!data.Sequences.TryGetValue(set, out var sequence))
        {
            throw new InvalidOperationException($"climb animation sequence absent: set={set}");
        }

        if (sequence.Records.Count == 0)
        {
            throw new InvalidOperationException($"climb animation sequence empty: set={set}");
        }

        var total = 0;
        foreach (var record in sequence.Records)
        {
            total += record.Duration;
        }

        if (total <= 0)
        {
            throw new InvalidOperationException($"climb animation sequence has invalid duration: set={set}");
        }

        var at = sequence.Loops ? ticks % total : Math.Min(ticks, total - 1);
        foreach (var record in sequence.Records)
        {
            if (at < record.Duration)
            {
                return new ClimbAnimationSample(record.Frame, record.OffsetX, record.OffsetY);
            }

            at -= record.Duration;
        }

        var last = sequence.Records[^1];
        return new ClimbAnimationSample(last.Frame, last.OffsetX, last.OffsetY);
    }
}
