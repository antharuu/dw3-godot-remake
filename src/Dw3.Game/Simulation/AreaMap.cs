using System.Numerics;

namespace Dw3Production.Simulation;

/// <summary>
/// Immutable byte grid of battle area cells for one field zone. Each cell holds the
/// battle area index (1..4), or 0 for "no area". Mirrors FIELD_MAP_AREAS in the source
/// map layout. The lookup truncates the fixed-point position with &gt;&gt; 8 (floor).
/// </summary>
public sealed class AreaMap
{
    private readonly byte[] _cells;

    /// <summary>Initializes a new instance of the <see cref="AreaMap"/> class.</summary>
    /// <param name="width">Map width in tiles; must be positive.</param>
    /// <param name="height">Map height in tiles; must be positive.</param>
    /// <param name="cells">Flat row-major byte array; length must equal width × height.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="width"/> or <paramref name="height"/> is not positive.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="cells"/> length does not equal width × height.</exception>
    public AreaMap(int width, int height, byte[] cells)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        if (cells.Length != checked(width * height))
        {
            throw new ArgumentException("area cell count does not match dimensions", nameof(cells));
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
    /// The battle area (1..4) at the actor's tile, or 0 (no area). Mirrors
    /// FIELDSTG_getMapCell(FIELD_MAP_AREAS): the fixed-point tile is truncated with &gt;&gt; 8.
    /// </summary>
    /// <param name="position">World position; fractional parts are truncated (floor) to get the tile.</param>
    /// <returns>Battle area index 1..4, or 0 for no area.</returns>
    public byte CellAt(Vector2 position)
    {
        var x = Math.Clamp((int)position.X, 0, Width - 1);
        var y = Math.Clamp((int)position.Y, 0, Height - 1);
        return _cells[y * Width + x];
    }
}
