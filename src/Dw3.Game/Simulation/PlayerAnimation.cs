using Dw3Production.Conversion;

namespace Dw3Production.Simulation;

public readonly record struct AnimationSample(int Frame, int OffsetX, int OffsetY, int FootGroundX, int FootGroundY, int SourceDirection, bool FlipH);

public static class PlayerAnimation
{
    public static AnimationSample Sample(PlayerData player, GameState state)
    {
        var (direction, flipH) = ResolveSpriteDirection(state.AnimationDirection);
        var sequence = player.Sequences.SingleOrDefault(candidate => candidate.Set == state.AnimationSet && candidate.Direction == direction)
            ?? throw new InvalidOperationException($"animation sequence absent: set={state.AnimationSet} direction={direction}");
        if (sequence.Records.Count == 0) throw new InvalidOperationException($"animation sequence empty: set={state.AnimationSet} direction={direction}");

        var total = sequence.Records.Sum(record => record.Duration);
        if (total <= 0) throw new InvalidOperationException($"animation sequence has invalid duration: set={state.AnimationSet} direction={direction}");
        var at = state.AnimationTicks % total;
        foreach (var record in sequence.Records)
        {
            if (at < record.Duration)
                return new AnimationSample(record.Frame, record.OffsetX, record.OffsetY, record.FootGroundX, record.FootGroundY, direction, flipH);
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
