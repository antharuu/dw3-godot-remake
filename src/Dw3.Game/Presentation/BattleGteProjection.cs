namespace Dw3Production.Presentation;

/// <summary>
/// The two hardware rules a source model polygon goes through between its view-space corners and the screen:
/// the GTE perspective transfer (<c>RTPS</c>), whose division saturates once a corner comes within half the
/// projection distance of the eye (or behind it), and the GPU, which skips any polygon spanning more than 1023
/// pixels across or 511 down. The source mesh drawer does no other near-plane test, so a corner close to or behind
/// the eye gives a saturated screen point and, in practice, a polygon the GPU refuses.
/// </summary>
internal static class BattleGteProjection
{
    /// <summary>Source screen X offset (OFX) of the 320-pixel battle screen.</summary>
    public const int OffsetX = 160;

    /// <summary>Source screen Y offset (OFY) of the 240-line battle screen.</summary>
    public const int OffsetY = 120;

    /// <summary>Widest polygon span the GPU draws, in pixels.</summary>
    public const int GpuMaxWidth = 1023;

    /// <summary>Tallest polygon span the GPU draws, in lines.</summary>
    public const int GpuMaxHeight = 511;

    private const int ScreenMin = -0x400;

    private const int ScreenMax = 0x3FF;

    private const int QuotientMax = 0x1FFFF;

    /// <summary>Returns whether the GTE division saturates for a corner at this view depth.</summary>
    /// <param name="depth">View depth in source units (positive in front of the eye).</param>
    /// <param name="projection">Projection distance H in source pixels.</param>
    /// <returns>Whether the corner overflows the perspective division.</returns>
    public static bool Overflows(int depth, int projection) => projection >= SaturateDepth(depth) * 2;

    /// <summary>Projects one view-space corner as the GTE does, with the source screen offsets.</summary>
    /// <param name="x">View-space X in source units (right).</param>
    /// <param name="y">View-space Y in source units (down).</param>
    /// <param name="depth">View depth in source units (positive in front of the eye).</param>
    /// <param name="projection">Projection distance H in source pixels.</param>
    /// <returns>The saturated absolute screen point.</returns>
    public static (int X, int Y) Project(int x, int y, int depth, int projection)
    {
        int z = SaturateDepth(depth);
        long quotient = Overflows(depth, projection) ? QuotientMax : Math.Min(QuotientMax, ((((long)projection * 0x20000) / z) + 1) / 2);
        int screenX = (int)Math.Clamp(((Saturate16(x) * quotient) >> 16) + OffsetX, ScreenMin, ScreenMax);
        int screenY = (int)Math.Clamp(((Saturate16(y) * quotient) >> 16) + OffsetY, ScreenMin, ScreenMax);
        return (screenX, screenY);
    }

    /// <summary>Returns whether the GPU draws a polygon with these screen corners.</summary>
    /// <param name="corners">Screen corners in source pixels.</param>
    /// <returns>Whether every pair of corners lies within the GPU's span limits.</returns>
    public static bool GpuDraws(IReadOnlyList<(int X, int Y)> corners)
    {
        ArgumentNullException.ThrowIfNull(corners);
        int width = corners.Max(corner => corner.X) - corners.Min(corner => corner.X);
        int height = corners.Max(corner => corner.Y) - corners.Min(corner => corner.Y);
        return width <= GpuMaxWidth && height <= GpuMaxHeight;
    }

    /// <summary>Saturates a view depth to the unsigned 16-bit SZ register.</summary>
    /// <param name="depth">View depth in source units.</param>
    /// <returns>The register value.</returns>
    private static int SaturateDepth(int depth) => Math.Clamp(depth, 0, 0xFFFF);

    /// <summary>Saturates a view coordinate to a signed 16-bit IR register.</summary>
    /// <param name="value">View coordinate in source units.</param>
    /// <returns>The register value.</returns>
    private static long Saturate16(int value) => Math.Clamp(value, short.MinValue, short.MaxValue);
}
