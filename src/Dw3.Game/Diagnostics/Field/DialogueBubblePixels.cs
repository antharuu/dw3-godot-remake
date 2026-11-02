using Godot;

namespace Dw3Production.Diagnostics.Field;

/// <summary>
/// Pixel measurements of the talk box in viewport captures. Talk-box sprites are drawn at 3x,
/// so each source texel is checked at the centre pixel of its 3x3 viewport block.
/// </summary>
internal static class DialogueBubblePixels
{
    /// <summary>Counts pixels of a region whose colour changed between two captures.</summary>
    /// <param name="before">Capture without the talk box.</param>
    /// <param name="after">Capture with the talk box.</param>
    /// <param name="region">Viewport region, clamped to the capture.</param>
    /// <returns>The number of changed pixels.</returns>
    public static int CountChanged(Image before, Image after, Rect2 region) =>
        CountRegion(after, region, (x, y) => ProbePixels.DifferentRgb(before.GetPixel(x, y), after.GetPixel(x, y)));

    /// <summary>
    /// Counts dark-blue pixels, blue above red and green by more than 0.12: the talk-box text ink.
    /// White or grey pixels never qualify, so this proves the text is not drawn white.
    /// </summary>
    /// <param name="image">Capture.</param>
    /// <param name="region">Viewport region, clamped to the capture.</param>
    /// <returns>The number of dark-blue pixels.</returns>
    public static int CountDarkBlue(Image image, Rect2 region) =>
        CountColor(image, region, c => c.B > c.R + 0.12f && c.B > c.G + 0.12f);

    /// <summary>
    /// Counts light-blue pixels: blue above both other channels and a channel sum above 1.35,
    /// which keeps the panel (222, 238, 255) blended over the scene and excludes the darker ink.
    /// </summary>
    /// <param name="image">Capture.</param>
    /// <param name="region">Viewport region, clamped to the capture.</param>
    /// <returns>The number of light-blue pixels.</returns>
    public static int CountLightBlue(Image image, Rect2 region) =>
        CountColor(image, region, c => c.B > c.R && c.B > c.G && c.R + c.G + c.B > 1.35f);

    /// <summary>
    /// Counts pixels close to the opaque panel body colour (222, 238, 255): R above 0.8, G above
    /// 0.85, B above 0.9 and blue-dominant. A half blend over the scene is too dark to qualify,
    /// so a high count proves the body is opaque.
    /// </summary>
    /// <param name="image">Capture.</param>
    /// <param name="region">Viewport region, clamped to the capture.</param>
    /// <returns>The number of body-coloured pixels.</returns>
    public static int CountBodyBlue(Image image, Rect2 region) =>
        CountColor(image, region, c => c.R > 0.8f && c.G > 0.85f && c.B > 0.9f && c.B > c.R);

    /// <summary>
    /// Counts opaque frame texels whose viewport pixel changed between the captures. A flat
    /// rectangle would also light up the frame's transparent gaps, which are not counted.
    /// </summary>
    /// <param name="before">Capture without the talk box.</param>
    /// <param name="after">Capture with the talk box.</param>
    /// <param name="frame">Frame sprite.</param>
    /// <param name="viewportTopLeft">Viewport pixel of the sprite's top-left corner.</param>
    /// <returns>The number of changed opaque texels.</returns>
    public static int CountSpriteOpaqueChanged(Image before, Image after, Texture2D frame, Vector2 viewportTopLeft) =>
        CountSpriteTexels(before, after, frame, viewportTopLeft, (_, rendered, back) => ProbePixels.DifferentRgb(back, rendered));

    /// <summary>
    /// Counts opaque frame texels rendered closer to the half blend of the texel with the scene
    /// than to the texel itself, the signature of a semi-transparent draw.
    /// </summary>
    /// <param name="before">Capture without the talk box.</param>
    /// <param name="after">Capture with the talk box.</param>
    /// <param name="frame">Frame sprite.</param>
    /// <param name="viewportTopLeft">Viewport pixel of the sprite's top-left corner.</param>
    /// <returns>The number of half-blended texels.</returns>
    public static int CountSpriteSemiBlended(Image before, Image after, Texture2D frame, Vector2 viewportTopLeft) =>
        CountSpriteTexels(before, after, frame, viewportTopLeft, (texel, rendered, back) => Distance(rendered, HalfBlend(texel, back)) < Distance(rendered, texel));

    /// <summary>
    /// Counts opaque frame texels rendered closer to the texel itself than to its half blend with
    /// the scene, the complement of <see cref="CountSpriteSemiBlended"/> for an opaque draw.
    /// </summary>
    /// <param name="before">Capture without the talk box.</param>
    /// <param name="after">Capture with the talk box.</param>
    /// <param name="frame">Frame sprite.</param>
    /// <param name="viewportTopLeft">Viewport pixel of the sprite's top-left corner.</param>
    /// <returns>The number of pure texels.</returns>
    public static int CountSpriteOpaquePure(Image before, Image after, Texture2D frame, Vector2 viewportTopLeft) =>
        CountSpriteTexels(before, after, frame, viewportTopLeft, (texel, rendered, back) => Distance(rendered, texel) < Distance(rendered, HalfBlend(texel, back)));

    /// <summary>Counts the pixels of a region that satisfy a test.</summary>
    /// <param name="image">Capture, whose bounds clamp the region.</param>
    /// <param name="region">Viewport region; its edges are rounded outward.</param>
    /// <param name="test">Test on a pixel column and row.</param>
    /// <returns>The number of pixels passing the test.</returns>
    private static int CountRegion(Image image, Rect2 region, Func<int, int, bool> test)
    {
        var minX = Math.Max(0, (int)MathF.Floor(region.Position.X));
        var maxX = Math.Min(image.GetWidth() - 1, (int)MathF.Ceiling(region.End.X));
        var minY = Math.Max(0, (int)MathF.Floor(region.Position.Y));
        var maxY = Math.Min(image.GetHeight() - 1, (int)MathF.Ceiling(region.End.Y));
        var count = 0;
        for (var y = minY; y <= maxY; y++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                count += test(x, y) ? 1 : 0;
            }
        }

        return count;
    }

    /// <summary>Counts the pixels of a region whose colour satisfies a test.</summary>
    /// <param name="image">Capture.</param>
    /// <param name="region">Viewport region.</param>
    /// <param name="test">Colour test.</param>
    /// <returns>The number of pixels passing the test.</returns>
    private static int CountColor(Image image, Rect2 region, Func<Color, bool> test) =>
        CountRegion(image, region, (x, y) => test(image.GetPixel(x, y)));

    /// <summary>Counts the opaque texels of a sprite whose rendering satisfies a test.</summary>
    /// <param name="before">Capture without the talk box.</param>
    /// <param name="after">Capture with the talk box.</param>
    /// <param name="frame">Frame sprite; an unreadable image counts nothing.</param>
    /// <param name="viewportTopLeft">Viewport pixel of the sprite's top-left corner.</param>
    /// <param name="test">Test on the texel colour, its rendered pixel and the pixel before the talk box.</param>
    /// <returns>The number of texels passing the test; texels whose pixel lies off the capture are skipped.</returns>
    private static int CountSpriteTexels(Image before, Image after, Texture2D frame, Vector2 viewportTopLeft, Func<Color, Color, Color, bool> test)
    {
        var image = frame.GetImage();
        if (image is null || image.IsEmpty())
        {
            return 0;
        }

        var count = 0;
        foreach (var (x, y, texel) in ProbePixels.OpaqueTexels(image))
        {
            var sx = (int)viewportTopLeft.X + (x * 3) + 1;
            var sy = (int)viewportTopLeft.Y + (y * 3) + 1;
            if (ProbePixels.Contains(after, sx, sy) && test(texel, after.GetPixel(sx, sy), before.GetPixel(sx, sy)))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>Mixes a texel half and half with the scene pixel under it, as a semi-transparent draw does.</summary>
    /// <param name="texel">Sprite texel.</param>
    /// <param name="back">Scene pixel.</param>
    /// <returns>The blended colour.</returns>
    private static Color HalfBlend(Color texel, Color back) =>
        new((texel.R * 0.5f) + (back.R * 0.5f), (texel.G * 0.5f) + (back.G * 0.5f), (texel.B * 0.5f) + (back.B * 0.5f));

    /// <summary>Sums the absolute channel differences of two colours, alpha ignored.</summary>
    /// <param name="left">First colour.</param>
    /// <param name="right">Second colour.</param>
    /// <returns>The distance in 0-1 channel units, 0..3.</returns>
    private static float Distance(Color left, Color right) =>
        MathF.Abs(left.R - right.R) + MathF.Abs(left.G - right.G) + MathF.Abs(left.B - right.B);
}
