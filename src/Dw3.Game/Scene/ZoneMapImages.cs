using Dw3Production.Conversion;
using Dw3Production.Simulation;
using Godot;

namespace Dw3Production.Scene;

/// <summary>
/// Reads the three single-channel map planes of a zone from the PNG files of the data pack:
/// the floor (walkability and slope rows), the triggers (warp, climb and drop slots) and the
/// optional battle areas. Each pixel's red byte is one cell value, row-major from the top-left.
/// </summary>
internal static class ZoneMapImages
{
    /// <summary>Error code of an unreadable image.</summary>
    private const string Unavailable = "CACHE_RESOURCE_UNAVAILABLE";

    /// <summary>Loads the floor plane.</summary>
    /// <param name="zone">Zone contract naming the image and its expected size.</param>
    /// <returns>The floor grid.</returns>
    /// <exception cref="CacheResourceException">The image is unreadable or its size differs from the zone.</exception>
    public static FloorMap LoadFloor(Zone zone) =>
        new(zone.Width, zone.Height, LoadCells(zone.FloorCellsPath, zone, "floor"));

    /// <summary>Loads the trigger plane.</summary>
    /// <param name="zone">Zone contract naming the image and its expected size.</param>
    /// <returns>The trigger grid.</returns>
    /// <exception cref="CacheResourceException">The image is unreadable or its size differs from the zone.</exception>
    public static TriggerMap LoadTriggers(Zone zone) =>
        new(zone.Width, zone.Height, LoadCells(zone.TriggerCellsPath, zone, "trigger"));

    /// <summary>Loads the battle-area plane.</summary>
    /// <param name="zone">Zone contract naming the image and its expected size.</param>
    /// <returns>The area grid, or <see langword="null"/> for a safe zone that has no battle areas.</returns>
    /// <exception cref="CacheResourceException">The image is unreadable or its size differs from the zone.</exception>
    public static AreaMap? LoadAreas(Zone zone) =>
        zone.AreasCellsPath is null ? null : new AreaMap(zone.Width, zone.Height, LoadCells(zone.AreasCellsPath, zone, "areas"));

    /// <summary>Reads one plane image into a flat row-major cell array.</summary>
    /// <param name="path">PNG path in the data pack.</param>
    /// <param name="zone">Zone whose width and height the image must have.</param>
    /// <param name="kind">Plane name used in error messages.</param>
    /// <returns><c>Width * Height</c> cell values.</returns>
    /// <exception cref="CacheResourceException">The image is unreadable (<c>CACHE_RESOURCE_UNAVAILABLE</c>) or its size differs (<c>CACHE_INVALID</c>).</exception>
    private static byte[] LoadCells(string path, Zone zone, string kind)
    {
        var image = Image.LoadFromFile(path);
        if (image is null || image.IsEmpty())
        {
            throw new CacheResourceException(Unavailable, $"{kind} image unreadable: {path}");
        }

        if (image.GetWidth() != zone.Width || image.GetHeight() != zone.Height)
        {
            throw new CacheResourceException("CACHE_INVALID", $"{kind} image dimensions mismatch: {path}");
        }

        var cells = new byte[zone.Width * zone.Height];
        for (var y = 0; y < zone.Height; y++)
        {
            for (var x = 0; x < zone.Width; x++)
            {
                cells[(y * zone.Width) + x] = (byte)image.GetPixel(x, y).R8;
            }
        }

        return cells;
    }
}
