using Dw3Production.Conversion;
using NumericsVector2 = System.Numerics.Vector2;

namespace Dw3Production.Simulation;

/// <summary>
/// Finds the conversation partner the player addresses with Cross: the first dialogue actor of
/// the zone whose interaction box contains the tile in front of the player.
/// </summary>
internal static class DialogueActorLocator
{
    /// <summary>
    /// World-unit step toward the tile in front of an actor for each of the eight source facings,
    /// starting with 0 = down and turning counter-clockwise; the diagonals are 11 units on each axis.
    /// </summary>
    private static readonly NumericsVector2[] DirectionSteps =
    [
        new(0, 16), new(-11, 11), new(-16, 0), new(-11, -11),
        new(0, -16), new(11, -11), new(16, 0), new(11, 11),
    ];

    /// <summary>Returns the tile in front of the player: its truncated tile plus the step of its facing.</summary>
    /// <param name="state">Player state.</param>
    /// <returns>The faced tile, in world units.</returns>
    public static NumericsVector2 FacingTile(GameState state)
    {
        var tile = new NumericsVector2((int)MathF.Floor(state.Position.X), (int)MathF.Floor(state.Position.Y));
        return tile + DirectionSteps[state.Facing];
    }

    /// <summary>
    /// Returns the first actor whose box holds a tile. The box spans half the actor width on each
    /// side of its X and a quarter of that width above and below its Y, bounds included.
    /// </summary>
    /// <param name="zone">Dialogue actors of the zone, in table order.</param>
    /// <param name="pos">Tile to test, in world units.</param>
    /// <returns>The actor, or <see langword="null"/>; actors without a width never match.</returns>
    public static DialogueActor? FindAt(ZoneDialogue zone, NumericsVector2 pos) =>
        zone.Actors.FirstOrDefault(actor => actor.Width is { } width && Contains(actor, width, pos));

    /// <summary>Tests one actor box.</summary>
    /// <param name="actor">Actor.</param>
    /// <param name="width">Actor width in world units.</param>
    /// <param name="pos">Tile to test.</param>
    /// <returns><see langword="true"/> when the tile is inside the box.</returns>
    private static bool Contains(DialogueActor actor, int width, NumericsVector2 pos)
    {
        var halfWidth = width >> 1;
        var halfHeight = halfWidth >> 1;
        return pos.X >= actor.X - halfWidth && actor.X + halfWidth >= pos.X
            && pos.Y >= actor.Y - halfHeight && actor.Y + halfHeight >= pos.Y;
    }
}
