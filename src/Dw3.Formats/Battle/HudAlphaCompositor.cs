namespace Dw3.Formats.Battle;

/// <summary>
/// Composites rendered sprite parts into one frame image with the same blending as the
/// reference PNG pipeline (PIL <c>alpha_composite</c>).
/// </summary>
public static class HudAlphaCompositor
{
    /// <summary>
    /// Composites <paramref name="parts"/> onto a canvas sized to their union bounding box.
    /// SpriteDrawer draws backward, so the last part is drawn first and ends up deepest.
    /// </summary>
    /// <param name="parts">Rendered parts in frame-record order; at least one.</param>
    /// <returns>The composed image and the bounding box origin relative to the frame anchor.</returns>
    public static (HudRgbaImage Image, int MinX, int MinY) ComposeBackToFront(IReadOnlyList<HudRenderedPart> parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        int minX = parts.Min(r => r.Dx);
        int minY = parts.Min(r => r.Dy);
        int maxX = parts.Max(r => r.Dx + r.Image.Width);
        int maxY = parts.Max(r => r.Dy + r.Image.Height);
        int w = maxX - minX;
        int h = maxY - minY;
        var canvas = new HudRgbaImage(new byte[w * h * 4], w, h);
        for (int i = parts.Count - 1; i >= 0; i--)
        {
            HudRenderedPart part = parts[i];
            Composite(canvas, part.Image, part.Dx - minX, part.Dy - minY);
        }

        return (canvas, minX, minY);
    }

    /// <summary>Draws <paramref name="source"/> over <paramref name="destination"/> with its top-left corner at (x, y), clipping at the edges.</summary>
    /// <param name="destination">Canvas modified in place.</param>
    /// <param name="source">Image drawn on top.</param>
    /// <param name="x">Destination column of the source's left edge; may be negative.</param>
    /// <param name="y">Destination row of the source's top edge; may be negative.</param>
    public static void Composite(HudRgbaImage destination, HudRgbaImage source, int x, int y)
    {
        for (int sy = 0; sy < source.Height; sy++)
        {
            int dy = y + sy;
            if (dy >= 0 && dy < destination.Height)
            {
                CompositeRow(destination, source, x, dy, sy);
            }
        }
    }

    /// <summary>Draws source row <paramref name="sy"/> onto destination row <paramref name="dy"/>.</summary>
    /// <param name="destination">Canvas modified in place.</param>
    /// <param name="source">Image drawn on top.</param>
    /// <param name="x">Destination column of the source's left edge.</param>
    /// <param name="dy">Destination row, already inside the canvas.</param>
    /// <param name="sy">Source row.</param>
    private static void CompositeRow(HudRgbaImage destination, HudRgbaImage source, int x, int dy, int sy)
    {
        for (int sx = 0; sx < source.Width; sx++)
        {
            int dx = x + sx;
            if (dx >= 0 && dx < destination.Width)
            {
                BlendPixel(destination.Pixels, ((dy * destination.Width) + dx) * 4, source.Pixels, ((sy * source.Width) + sx) * 4);
            }
        }
    }

    /// <summary>
    /// Blends one source pixel over one destination pixel. Transparent sources are skipped, opaque
    /// sources or empty destinations are copied, anything else uses dst = src + dst × (1 − alpha).
    /// </summary>
    /// <param name="dst">Destination RGBA buffer.</param>
    /// <param name="dstAt">Byte offset of the destination pixel.</param>
    /// <param name="src">Source RGBA buffer.</param>
    /// <param name="srcAt">Byte offset of the source pixel.</param>
    private static void BlendPixel(byte[] dst, int dstAt, byte[] src, int srcAt)
    {
        byte sa = src[srcAt + 3];
        if (sa == 0)
        {
            return;
        }

        if (sa == 255 || dst[dstAt + 3] == 0)
        {
            dst[dstAt] = src[srcAt];
            dst[dstAt + 1] = src[srcAt + 1];
            dst[dstAt + 2] = src[srcAt + 2];
            dst[dstAt + 3] = sa;
            return;
        }

        // PIL alpha_composite: dst = src + dst*(1-alpha/255)
        float alpha = sa / 255f;
        float inv = 1f - alpha;
        float da = dst[dstAt + 3] / 255f;
        float outA = alpha + (da * inv);
        if (outA > 0)
        {
            dst[dstAt] = (byte)MathF.Round(((src[srcAt] * alpha) + (dst[dstAt] * da * inv)) / outA);
            dst[dstAt + 1] = (byte)MathF.Round(((src[srcAt + 1] * alpha) + (dst[dstAt + 1] * da * inv)) / outA);
            dst[dstAt + 2] = (byte)MathF.Round(((src[srcAt + 2] * alpha) + (dst[dstAt + 2] * da * inv)) / outA);
            dst[dstAt + 3] = (byte)MathF.Round(outA * 255);
        }
    }
}
