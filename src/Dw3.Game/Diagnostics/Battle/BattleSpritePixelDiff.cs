using Godot;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>Compares two same-sized captures of one frame, drawn with and without the sprite layers.</summary>
/// <param name="Changed">Pixels whose colour differs by more than the threshold on any channel.</param>
/// <param name="MinX">Left edge of the changed area, or -1.</param>
/// <param name="MinY">Top edge of the changed area, or -1.</param>
/// <param name="MaxX">Right edge of the changed area, or -1.</param>
/// <param name="MaxY">Bottom edge of the changed area, or -1.</param>
/// <param name="White">Pixels of the first capture at or above 250 on every channel.</param>
/// <param name="Total">Pixels compared.</param>
internal readonly record struct BattleSpritePixelDiff(int Changed, int MinX, int MinY, int MaxX, int MaxY, int White, int Total)
{
    /// <summary>Per-channel difference treated as a real change.</summary>
    private const int Threshold = 8;

    /// <summary>Measures the pixels a sprite layer actually contributes.</summary>
    /// <param name="with">Capture with the layers drawn.</param>
    /// <param name="without">The same frame with the layers hidden.</param>
    /// <returns>The difference summary.</returns>
    public static BattleSpritePixelDiff Measure(Image with, Image without)
    {
        ArgumentNullException.ThrowIfNull(with);
        ArgumentNullException.ThrowIfNull(without);
        using Image a = (Image)with.Duplicate();
        using Image b = (Image)without.Duplicate();
        a.Convert(Image.Format.Rgba8);
        b.Convert(Image.Format.Rgba8);
        byte[] left = a.GetData();
        byte[] right = b.GetData();
        int width = a.GetWidth();
        var box = (Changed: 0, MinX: int.MaxValue, MinY: int.MaxValue, MaxX: -1, MaxY: -1, White: 0);
        for (int i = 0; i + 3 < left.Length && i + 3 < right.Length; i += 4)
        {
            box.White += left[i] >= 250 && left[i + 1] >= 250 && left[i + 2] >= 250 ? 1 : 0;
            if (Differs(left, right, i))
            {
                int x = (i / 4) % width;
                int y = i / 4 / width;
                box = (box.Changed + 1, Math.Min(box.MinX, x), Math.Min(box.MinY, y), Math.Max(box.MaxX, x), Math.Max(box.MaxY, y), box.White);
            }
        }

        return box.Changed == 0
            ? new BattleSpritePixelDiff(0, -1, -1, -1, -1, box.White, left.Length / 4)
            : new BattleSpritePixelDiff(box.Changed, box.MinX, box.MinY, box.MaxX, box.MaxY, box.White, left.Length / 4);
    }

    /// <summary>Tests one RGB pixel pair.</summary>
    /// <param name="left">First capture bytes.</param>
    /// <param name="right">Second capture bytes.</param>
    /// <param name="i">Pixel byte offset.</param>
    /// <returns>Whether any channel differs beyond the threshold.</returns>
    private static bool Differs(byte[] left, byte[] right, int i) =>
        Math.Abs(left[i] - right[i]) > Threshold || Math.Abs(left[i + 1] - right[i + 1]) > Threshold || Math.Abs(left[i + 2] - right[i + 2]) > Threshold;
}
