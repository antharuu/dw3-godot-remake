using System.Numerics;

namespace Dw3Production.Simulation;

/// <summary>
/// Immutable byte grid of warp trigger cells for one field zone. Each cell encodes
/// a slot index (bits 4..0) and a source facing direction (bits 7..5). Cell 0 means
/// no trigger. Mirrors FIELD_MAP_TRIGGERS in the source map layout.
/// </summary>
public sealed class TriggerMap
{
    private readonly byte[] _cells;

    /// <summary>Initializes a new instance of the <see cref="TriggerMap"/> class.</summary>
    /// <param name="width">Map width in tiles; must be positive.</param>
    /// <param name="height">Map height in tiles; must be positive.</param>
    /// <param name="cells">Flat row-major byte array; length must equal width × height.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="width"/> or <paramref name="height"/> is not positive.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="cells"/> length does not equal width × height.</exception>
    public TriggerMap(int width, int height, byte[] cells)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        if (cells.Length != checked(width * height))
        {
            throw new ArgumentException("trigger cell count does not match dimensions", nameof(cells));
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
    /// Returns the raw trigger byte at the tile nearest to <paramref name="position"/>, clamped to the map bounds.
    /// </summary>
    /// <param name="position">World position; fractional parts are rounded to the nearest tile.</param>
    /// <returns>The raw trigger byte (0 = no trigger).</returns>
    public byte CellAt(Vector2 position)
    {
        var x = Math.Clamp((int)MathF.Round(position.X), 0, Width - 1);
        var y = Math.Clamp((int)MathF.Round(position.Y), 0, Height - 1);
        return _cells[y * Width + x];
    }

    /// <summary>
    /// Enumerates all non-empty cells whose tile centre lies within <paramref name="radius"/> of
    /// <paramref name="position"/>. Tiles outside the map are never returned.
    /// </summary>
    /// <param name="position">World position of the query origin.</param>
    /// <param name="radius">Search radius in world units.</param>
    /// <returns>Each matching cell with its tile-centre position and raw byte value.</returns>
    public IEnumerable<(Vector2 Position, byte Raw)> CellsWithin(Vector2 position, float radius)
    {
        var minX = Math.Max(0, (int)MathF.Ceiling(position.X - radius));
        var maxX = Math.Min(Width - 1, (int)MathF.Floor(position.X + radius));
        var minY = Math.Max(0, (int)MathF.Ceiling(position.Y - radius));
        var maxY = Math.Min(Height - 1, (int)MathF.Floor(position.Y + radius));
        for (var y = minY; y <= maxY; y++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                var candidate = new Vector2(x, y);
                if (Vector2.Distance(position, candidate) <= radius)
                {
                    yield return (candidate, _cells[y * Width + x]);
                }
            }
        }
    }
}
