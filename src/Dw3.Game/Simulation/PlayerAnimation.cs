using Dw3Production.Conversion;

namespace Dw3Production.Simulation;

/// <summary>
/// Samples the current animation frame from a player data archive given the game state.
/// Selects the sequence that matches the active animation set and resolved source direction,
/// advances through its records by accumulated tick duration, and returns a fully resolved
/// <see cref="AnimationSample"/>. Engine-free; depends only on the conversion data types and
/// the simulation game state.
/// </summary>
public static class PlayerAnimation
{
    /// <summary>
    /// Returns the animation sample for the given player and state. Throws when no matching
    /// sequence is found, when the sequence has no records, or when all record durations sum
    /// to zero.
    /// </summary>
    /// <param name="player">The player animation data decoded from the disc archive.</param>
    /// <param name="state">Current simulation state, read for AnimationSet, AnimationDirection and AnimationTicks.</param>
    /// <returns>The resolved <see cref="AnimationSample"/> for the current tick.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the sequence is absent, empty, has invalid duration, or the cadence selection fails.</exception>
    public static AnimationSample Sample(PlayerData player, GameState state)
    {
        var (direction, flipH) = ResolveSpriteDirection(state.AnimationDirection);
        var sequence = player.Sequences.SingleOrDefault(candidate => candidate.Set == state.AnimationSet && candidate.Direction == direction)
            ?? throw new InvalidOperationException($"animation sequence absent: set={state.AnimationSet} direction={direction}");
        if (sequence.Records.Count == 0)
        {
            throw new InvalidOperationException($"animation sequence empty: set={state.AnimationSet} direction={direction}");
        }

        var total = sequence.Records.Sum(record => record.Duration);
        if (total <= 0)
        {
            throw new InvalidOperationException($"animation sequence has invalid duration: set={state.AnimationSet} direction={direction}");
        }

        var at = state.AnimationTicks % total;
        foreach (var record in sequence.Records)
        {
            if (at < record.Duration)
            {
                return new AnimationSample(record.Frame, record.OffsetX, record.OffsetY, record.FootGroundX, record.FootGroundY, direction, flipH);
            }

            at -= record.Duration;
        }

        throw new InvalidOperationException("animation cadence selection failed");
    }

    private static (int Direction, bool FlipH) ResolveSpriteDirection(int direction) => direction switch
    {
        0 => (0, false),
        1 => (1, false),
        2 => (2, false),
        3 => (3, false),
        4 => (4, false),
        5 => (3, true),
        6 => (2, true),
        7 => (1, true),
        _ => throw new ArgumentOutOfRangeException(nameof(direction)),
    };
}
