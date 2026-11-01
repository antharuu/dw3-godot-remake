namespace Dw3.Formats.Field;

/// <summary>Decoded terrain pixel planes for one zone.</summary>
public sealed record ZoneTerrain(int TileColumns, int TileRows, IReadOnlyList<byte[]> Layers, byte[] Composite, IReadOnlyList<ZoneTileRecord> TileRecords)
{
    /// <summary>Gets the terrain width in pixels.</summary>
    public int Width => checked(TileColumns * 128);

    /// <summary>Gets the terrain height in pixels.</summary>
    public int Height => checked(TileRows * 128);
}
