using Dw3Production.Simulation;
using NumericsVector2 = System.Numerics.Vector2;

namespace Dw3Production.Diagnostics.Field;

/// <summary>
/// Finds the cells of a trigger slot in a zone trigger map. A trigger cell byte holds the slot
/// number in its low five bits and the admitted trigger direction in its high three bits; 0 is no
/// trigger.
/// </summary>
internal static class TriggerSlotLocator
{
    /// <summary>Mask of the slot number in a trigger cell byte.</summary>
    private const int SlotMask = 0x1F;

    /// <summary>Shift that moves the trigger direction bits down to 0-7.</summary>
    private const int DirectionShift = 5;

    /// <summary>Returns the first cell of a slot in row-major order.</summary>
    /// <param name="zone">Zone whose trigger map is scanned.</param>
    /// <param name="slot">Slot number.</param>
    /// <returns>The cell in world units.</returns>
    /// <exception cref="InvalidOperationException">The slot has no cell in the zone.</exception>
    public static NumericsVector2 FindCell(FieldZone zone, int slot) =>
        FindFirst(zone, slot)?.Cell ?? throw new InvalidOperationException($"trigger slot={slot} missing in zone={zone.Zone.Id}");

    /// <summary>Returns the trigger direction stored in the first cell of a slot.</summary>
    /// <param name="zone">Zone whose trigger map is scanned.</param>
    /// <param name="slot">Slot number.</param>
    /// <returns>The raw trigger direction, 0-7.</returns>
    /// <exception cref="InvalidOperationException">The slot has no cell in the zone.</exception>
    public static int FindDirection(FieldZone zone, int slot) =>
        FindFirst(zone, slot)?.Direction ?? throw new InvalidOperationException($"trigger slot={slot} missing in zone={zone.Zone.Id}");

    /// <summary>Returns the first cell of a climb or drop slot and its trigger direction.</summary>
    /// <param name="zone">Zone whose trigger map is scanned.</param>
    /// <param name="slot">Climb or drop slot number.</param>
    /// <returns>The cell in world units and its raw trigger direction.</returns>
    /// <exception cref="InvalidOperationException">The slot has no cell in the zone.</exception>
    public static (NumericsVector2 Cell, int Direction) FindClimbTrigger(FieldZone zone, int slot) =>
        FindFirst(zone, slot) ?? throw new InvalidOperationException($"climb slot={slot} trigger missing in zone={zone.Zone.Id}");

    /// <summary>
    /// Picks the walking target just outside a slot cluster from where the player stands. From the
    /// north-east the target is the southernmost cell, one unit south, so the path follows the east
    /// wall; from the east it is the southern cell of the east column, one unit east; otherwise it is
    /// the nearest cell, stepped one unit outside towards the player.
    /// </summary>
    /// <param name="zone">Zone whose trigger map is scanned.</param>
    /// <param name="slot">Exit slot number.</param>
    /// <param name="start">Player position in world units.</param>
    /// <returns>The approach target in world units.</returns>
    /// <exception cref="InvalidOperationException">The slot has no cell in the zone.</exception>
    public static NumericsVector2 FindApproachCell(FieldZone zone, int slot, NumericsVector2 start)
    {
        var cells = FindAll(zone, slot);
        if (cells.Count == 0)
        {
            throw new InvalidOperationException($"trigger slot={slot} missing in zone={zone.Zone.Id}");
        }

        var minX = cells.Min(c => c.X);
        var maxX = cells.Max(c => c.X);
        var minY = cells.Min(c => c.Y);
        var maxY = cells.Max(c => c.Y);
        var fromNorth = start.Y < minY;
        var fromEast = start.X > maxX;
        var corner = ApproachCorner(cells, start, fromNorth, fromEast, maxX);
        var dx = OutsideStepX(start, fromNorth, fromEast, minX);
        var dy = OutsideStepY(start, fromNorth, maxY);
        return new NumericsVector2(corner.X + dx, corner.Y + dy);
    }

    /// <summary>Chooses the cluster cell the approach target is anchored to.</summary>
    /// <param name="cells">Every cell of the slot as integer coordinates, in row-major order.</param>
    /// <param name="start">Player position.</param>
    /// <param name="fromNorth">The player stands above the cluster.</param>
    /// <param name="fromEast">The player stands right of the cluster.</param>
    /// <param name="maxX">Rightmost cluster column.</param>
    /// <returns>The anchor cell.</returns>
    private static (int X, int Y) ApproachCorner(List<(int X, int Y)> cells, NumericsVector2 start, bool fromNorth, bool fromEast, int maxX)
    {
        if (fromNorth && fromEast)
        {
            return cells.OrderByDescending(c => c.Y).ThenByDescending(c => c.X).First();
        }

        if (fromEast)
        {
            return cells.Where(c => c.X == maxX).OrderBy(c => c.Y).LastOrDefault(cells[0]);
        }

        return cells.OrderBy(c => NumericsVector2.DistanceSquared(start, new NumericsVector2(c.X, c.Y))).First();
    }

    /// <summary>Returns the horizontal step that puts the target outside the cluster.</summary>
    /// <param name="start">Player position.</param>
    /// <param name="fromNorth">The player stands above the cluster.</param>
    /// <param name="fromEast">The player stands right of the cluster.</param>
    /// <param name="minX">Leftmost cluster column.</param>
    /// <returns>-1, 0 or 1 world unit.</returns>
    private static int OutsideStepX(NumericsVector2 start, bool fromNorth, bool fromEast, int minX)
    {
        if (fromEast)
        {
            return fromNorth ? 0 : 1;
        }

        return start.X < minX ? -1 : 0;
    }

    /// <summary>Returns the vertical step that puts the target outside the cluster.</summary>
    /// <param name="start">Player position.</param>
    /// <param name="fromNorth">The player stands above the cluster.</param>
    /// <param name="maxY">Bottom cluster row.</param>
    /// <returns>-1, 0 or 1 world unit.</returns>
    private static int OutsideStepY(NumericsVector2 start, bool fromNorth, int maxY)
    {
        if (fromNorth)
        {
            return 1;
        }

        return start.Y > maxY ? -1 : 0;
    }

    /// <summary>Returns every cell of a slot in row-major order.</summary>
    /// <param name="zone">Zone whose trigger map is scanned.</param>
    /// <param name="slot">Slot number.</param>
    /// <returns>The integer cell coordinates, empty when the slot is absent.</returns>
    private static List<(int X, int Y)> FindAll(FieldZone zone, int slot)
    {
        var cells = new List<(int X, int Y)>();
        foreach (var (cell, _) in Scan(zone, slot))
        {
            cells.Add(((int)cell.X, (int)cell.Y));
        }

        return cells;
    }

    /// <summary>Returns the first cell of a slot in row-major order with its direction.</summary>
    /// <param name="zone">Zone whose trigger map is scanned.</param>
    /// <param name="slot">Slot number.</param>
    /// <returns>The cell and direction, or <see langword="null"/> when the slot is absent.</returns>
    private static (NumericsVector2 Cell, int Direction)? FindFirst(FieldZone zone, int slot)
    {
        using var matches = Scan(zone, slot).GetEnumerator();
        return matches.MoveNext() ? matches.Current : null;
    }

    /// <summary>Lists the cells of a slot in row-major order with their trigger direction.</summary>
    /// <param name="zone">Zone whose trigger map is scanned.</param>
    /// <param name="slot">Slot number.</param>
    /// <returns>Each matching cell and the direction bits of its byte.</returns>
    private static IEnumerable<(NumericsVector2 Cell, int Direction)> Scan(FieldZone zone, int slot)
    {
        for (var y = 0; y < zone.Triggers.Height; y++)
        {
            for (var x = 0; x < zone.Triggers.Width; x++)
            {
                var raw = zone.Triggers.CellAt(new NumericsVector2(x, y));
                if (raw != 0 && (raw & SlotMask) == slot)
                {
                    yield return (new NumericsVector2(x, y), raw >> DirectionShift);
                }
            }
        }
    }
}
