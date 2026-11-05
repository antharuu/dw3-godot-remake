using Godot;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>Counts local brightness without diluting a small actor into a full black framebuffer.</summary>
internal static class BattleEntrancePixels
{
    /// <summary>Counts pixels with any RGB channel strictly above 96, ignoring alpha.</summary>
    /// <param name="pixels">Packed RGB or RGBA framebuffer bytes.</param>
    /// <param name="width">Framebuffer width.</param>
    /// <param name="stride">Bytes per pixel.</param>
    /// <param name="region">Clipped half-open actor rectangle.</param>
    /// <returns>The local bright-pixel count.</returns>
    public static int CountBright(byte[] pixels, int width, int stride, Rect2I region)
    {
        var count = 0;
        for (var y = region.Position.Y; y < region.End.Y; y++)
        {
            for (var x = region.Position.X; x < region.End.X; x++)
            {
                var offset = ((y * width) + x) * stride;
                if (Math.Max(pixels[offset], Math.Max(pixels[offset + 1], pixels[offset + 2])) > 96)
                {
                    count++;
                }
            }
        }

        return count;
    }
}
