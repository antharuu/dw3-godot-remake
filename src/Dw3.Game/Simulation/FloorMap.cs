using System.Numerics;

namespace Dw3Production.Simulation;

/// <summary>
/// Immutable byte grid of walkable floor cells for one field zone. Cell 0 is void; any
/// non-zero value is a classified slope row (low nibble) with an optional mirror flag
/// (bit 4). Mirrors the floor layer of the stage map on the disc; lookup truncates the
/// fixed-point position with &gt;&gt; 8 (floor).
/// </summary>
public sealed class FloorMap
{
    private readonly byte[] _cells;

    /// <summary>Initializes a new instance of the <see cref="FloorMap"/> class.</summary>
    /// <param name="width">Map width in tiles; must be positive.</param>
    /// <param name="height">Map height in tiles; must be positive.</param>
    /// <param name="cells">Flat row-major byte array; length must equal width × height.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="width"/> or <paramref name="height"/> is not positive.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="cells"/> length does not equal width × height.</exception>
    public FloorMap(int width, int height, byte[] cells)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        if (cells.Length != checked(width * height))
        {
            throw new ArgumentException("floor cell count does not match dimensions", nameof(cells));
        }

        Width = width;
        Height = height;
        _cells = [.. cells];
    }

    /// <summary>Gets the map width in tiles.</summary>
    public int Width { get; }

    /// <summary>Gets the map height in tiles.</summary>
    public int Height { get; }

    /// <summary>
    /// Returns true when <paramref name="position"/> is inside the map bounds (strictly less than Width/Height).
    /// </summary>
    /// <param name="position">World position to test.</param>
    /// <returns>True when the position is within the map rectangle.</returns>
    public bool Contains(Vector2 position) => position.X >= 0 && position.Y >= 0 && position.X < Width && position.Y < Height;

    /// <summary>
    /// Returns the raw floor byte at the tile containing <paramref name="position"/>.
    /// Truncates fractional parts (floor) to get the tile index, matching the original map cell lookup.
    /// </summary>
    /// <param name="position">World position; must be within the map bounds.</param>
    /// <returns>The raw floor cell byte (0 = void, non-zero = classified slope).</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="position"/> is outside the map bounds.</exception>
    public byte CellAt(Vector2 position)
    {
        if (!Contains(position))
        {
            throw new ArgumentOutOfRangeException(nameof(position));
        }

        // The source map lookup truncates its fixed-point tile position with >> 8.
        var x = Math.Clamp((int)position.X, 0, Width - 1);
        var y = Math.Clamp((int)position.Y, 0, Height - 1);
        return _cells[(y * Width) + x];
    }
}
