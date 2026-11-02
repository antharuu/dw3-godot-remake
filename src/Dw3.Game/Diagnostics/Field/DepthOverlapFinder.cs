using Dw3Production.Simulation;
using Godot;
using NumericsVector2 = System.Numerics.Vector2;

namespace Dw3Production.Diagnostics.Field;

/// <summary>
/// Searches a zone terrain layer for the first player placement, in row-major order of the sprite
/// top-left corner, where an opaque player texel lies over an opaque terrain texel of another colour.
/// </summary>
internal static class DepthOverlapFinder
{
    /// <summary>Finds the first placement whose overlap proves which layer is drawn on top.</summary>
    /// <param name="zone">Zone whose layer image is scanned.</param>
    /// <param name="sample">Player animation sample: frame image and sprite offset from the foot anchor.</param>
    /// <param name="playerImage">Decoded player frame.</param>
    /// <param name="terrainLayer">0 for the layer drawn behind the player, 1 for the layer drawn in front.</param>
    /// <returns>The placement, the texel and the colour expected on screen.</returns>
    /// <exception cref="InvalidOperationException">The layer image is unreadable or no texel overlaps.</exception>
    public static DepthOverlap Find(FieldZone zone, AnimationSample sample, Image playerImage, int terrainLayer)
    {
        var terrain = ProbePixels.TryLoad(zone.Zone.Layers[terrainLayer].Path)
            ?? throw new InvalidOperationException($"depth probe images unreadable zone={zone.Zone.Id} layer={terrainLayer}");
        var playerTexels = ProbePixels.OpaqueTexels(playerImage).ToList();
        for (var topY = 0; topY <= zone.Zone.Height - playerImage.GetHeight(); topY++)
        {
            for (var topX = 0; topX <= zone.Zone.Width - playerImage.GetWidth(); topX++)
            {
                if (TryOverlapAt(terrain, playerTexels, topX, topY, terrainLayer) is { } overlap)
                {
                    return new DepthOverlap(new NumericsVector2(topX - sample.OffsetX, topY - sample.OffsetY), overlap.WorldPixel, overlap.ExpectedForeground);
                }
            }
        }

        throw new InvalidOperationException($"depth probe found no opaque player/terrain overlap zone={zone.Zone.Id} layer={terrainLayer}");
    }

    /// <summary>Tests one sprite placement against the terrain.</summary>
    /// <param name="terrain">Terrain layer image in world pixels.</param>
    /// <param name="playerTexels">Opaque player texels in row-major order.</param>
    /// <param name="topX">World column of the sprite top-left corner.</param>
    /// <param name="topY">World row of the sprite top-left corner.</param>
    /// <param name="terrainLayer">0 when the player must win, 1 when the terrain must win.</param>
    /// <returns>The first overlapping texel, or <see langword="null"/>.</returns>
    private static (NumericsVector2 WorldPixel, Color ExpectedForeground)? TryOverlapAt(Image terrain, List<(int X, int Y, Color Color)> playerTexels, int topX, int topY, int terrainLayer)
    {
        foreach (var (px, py, playerPixel) in playerTexels)
        {
            var x = topX + px;
            var y = topY + py;
            var terrainPixel = terrain.GetPixel(x, y);
            if (ProbePixels.IsOpaque(terrainPixel) && ProbePixels.DifferentRgb(terrainPixel, playerPixel))
            {
                return (new NumericsVector2(x, y), terrainLayer == 0 ? playerPixel : terrainPixel);
            }
        }

        return null;
    }
}
