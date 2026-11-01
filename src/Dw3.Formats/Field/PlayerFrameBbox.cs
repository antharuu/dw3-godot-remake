namespace Dw3.Formats.Field;

/// <summary>
/// Result of a visible-bounding-box scan on a player TIM frame.
/// Uses PIL-compatible exclusive right/lower coordinates to match the
/// Python parity baseline exactly.
/// </summary>
public readonly record struct PlayerFrameBbox(int Left, int Upper, int RightExclusive, int LowerExclusive)
{
    /// <summary>
    /// Width using the Python visible_size formula:
    /// RightExclusive - Left + 1  (off-by-one vs the actual pixel count).
    /// </summary>
    public int VisibleSizeWidth => RightExclusive - Left + 1;

    /// <summary>
    /// Height using the Python visible_size formula:
    /// LowerExclusive - Upper + 1  (off-by-one vs the actual pixel count).
    /// </summary>
    public int VisibleSizeHeight => LowerExclusive - Upper + 1;
}
