namespace Dw3Production.Presentation;

/// <summary>
/// The source ordering-table entries of the battle models layer (4096 entries, OT shift 12), shared by the
/// fighter polygons and the sprite effects drawn on that layer. The GTE stores a projected depth as SZ / 4;
/// a polygon then drops two more bits per corner and averages them (entry = SZ / 16), while a projected sprite
/// anchor drops four (entry = SZ / 64). An entry is drawn from the farthest (largest) to the nearest, and within
/// one entry the primitive linked last is drawn first.
/// </summary>
public static class BattleSpriteOrdering
{
    /// <summary>The models layer's ordering-table size shift (4096 entries).</summary>
    public const int ModelsOtShift = 12;

    /// <summary>The entry of a screen sprite placed at the front of its layer (depth marker 0x7FFF).</summary>
    public const int FrontEntry = 0;

    /// <summary>The entry of a screen sprite placed at the back of its layer (depth marker -1).</summary>
    public const int BackEntry = 0xFFF;

    /// <summary>Converts a view depth to the value the GTE stores for ordering (SZ / 4).</summary>
    /// <param name="sz">Projected view depth in source units.</param>
    /// <returns>The stored ordering depth.</returns>
    public static int StoredDepth(int sz) => sz >> 2;

    /// <summary>Returns the entry of a projected sprite anchor: the stored depth shifted by 16 minus the OT shift.</summary>
    /// <param name="sz">Projected view depth of the anchor in source units.</param>
    /// <returns>The ordering-table entry.</returns>
    public static int PointEntry(int sz) => StoredDepth(sz) >> (16 - ModelsOtShift);

    /// <summary>Returns the entry of a fighter polygon: each corner's stored depth shifted by 14 minus the OT shift, then averaged.</summary>
    /// <param name="sz">Projected view depths of the three or four corners in source units.</param>
    /// <returns>The ordering-table entry.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The polygon is neither a triangle nor a quad.</exception>
    public static int PolygonEntry(IReadOnlyList<int> sz)
    {
        ArgumentNullException.ThrowIfNull(sz);
        int shift = 14 - ModelsOtShift;
        return sz.Count switch
        {
            3 => RenderOrderingTable.TriangleKey(StoredDepth(sz[0]), StoredDepth(sz[1]), StoredDepth(sz[2]), shift),
            4 => RenderOrderingTable.QuadKey(StoredDepth(sz[0]), StoredDepth(sz[1]), StoredDepth(sz[2]), StoredDepth(sz[3]), shift),
            _ => throw new ArgumentOutOfRangeException(nameof(sz), sz.Count, "a source polygon has three or four corners"),
        };
    }

    /// <summary>
    /// Places sprites in the polygons' draw order. A sprite is drawn after every polygon of a farther entry and
    /// before the polygons of its own entry and nearer ones: the effect task queues its primitives after the
    /// fighters' model tasks, so in a shared entry the sprite is linked last and drawn first.
    /// </summary>
    /// <param name="polygonEntries">Entries of the emitted polygons, in their draw order.</param>
    /// <param name="spriteEntry">Entry of one sprite.</param>
    /// <returns>The index of the first polygon drawn after the sprite, or the polygon count to draw it last.</returns>
    public static int InsertionPoint(IReadOnlyList<int> polygonEntries, int spriteEntry)
    {
        ArgumentNullException.ThrowIfNull(polygonEntries);
        for (int i = 0; i < polygonEntries.Count; i++)
        {
            if (polygonEntries[i] <= spriteEntry)
            {
                return i;
            }
        }

        return polygonEntries.Count;
    }
}
