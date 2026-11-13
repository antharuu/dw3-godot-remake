using Godot;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>
/// A per-pixel set of one viewport capture: the pixels that differ between two captures of the same
/// held frame, so a layer's footprint, a model's silhouette and their overlap can be intersected.
/// </summary>
internal sealed class BattleCompositionMask
{
    /// <summary>Per-channel difference treated as a real change, the threshold of the sprite pixel diff.</summary>
    private const int Threshold = 8;

    private readonly bool[] _pixels;

    /// <summary>Initializes a new instance of the <see cref="BattleCompositionMask"/> class.</summary>
    /// <param name="width">Capture width in pixels.</param>
    /// <param name="pixels">One flag per pixel, row-major.</param>
    private BattleCompositionMask(int width, bool[] pixels)
    {
        Width = width;
        _pixels = pixels;
    }

    /// <summary>Gets the capture width in pixels.</summary>
    public int Width { get; }

    /// <summary>Gets the number of pixels in the set.</summary>
    public int Count => _pixels.Count(pixel => pixel);

    /// <summary>Builds the set of pixels whose RGB differs between two captures of the same frame.</summary>
    /// <param name="with">Capture with the measured layer drawn.</param>
    /// <param name="without">The same frame with the layer hidden.</param>
    /// <returns>The changed pixels.</returns>
    public static BattleCompositionMask Changed(Image with, Image without)
    {
        ArgumentNullException.ThrowIfNull(with);
        ArgumentNullException.ThrowIfNull(without);
        byte[] left = Rgba(with);
        byte[] right = Rgba(without);
        var pixels = new bool[Math.Min(left.Length, right.Length) / 4];
        for (int i = 0; i < pixels.Length; i++)
        {
            int o = i * 4;
            pixels[i] = Math.Abs(left[o] - right[o]) > Threshold || Math.Abs(left[o + 1] - right[o + 1]) > Threshold || Math.Abs(left[o + 2] - right[o + 2]) > Threshold;
        }

        return new BattleCompositionMask(with.GetWidth(), pixels);
    }

    /// <summary>Returns the pixels present in both sets.</summary>
    /// <param name="other">Second set of the same capture size.</param>
    /// <returns>The intersection.</returns>
    public BattleCompositionMask And(BattleCompositionMask other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new BattleCompositionMask(Width, [.. _pixels.Select((pixel, i) => pixel && i < other._pixels.Length && other._pixels[i])]);
    }

    /// <summary>Returns the pixels of this set absent from another.</summary>
    /// <param name="other">Set to remove.</param>
    /// <returns>The difference.</returns>
    public BattleCompositionMask Except(BattleCompositionMask other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new BattleCompositionMask(Width, [.. _pixels.Select((pixel, i) => pixel && !(i < other._pixels.Length && other._pixels[i]))]);
    }

    /// <summary>Counts the pixels of the set inside a column range, the lateral margins of a 16:9 frame.</summary>
    /// <param name="fromX">First column, inclusive.</param>
    /// <param name="toX">Last column, exclusive.</param>
    /// <returns>Pixels of the set in those columns.</returns>
    public int CountColumns(int fromX, int toX) => _pixels.Where((pixel, i) => pixel && i % Width >= fromX && i % Width < toX).Count();

    /// <summary>Gets the centre of the set's bounding box.</summary>
    /// <returns>The centre in viewport pixels, or null for an empty set.</returns>
    public Vector2? Center()
    {
        int[] indices = [.. Enumerable.Range(0, _pixels.Length).Where(i => _pixels[i])];
        if (indices.Length == 0)
        {
            return null;
        }

        int minX = indices.Min(i => i % Width);
        int maxX = indices.Max(i => i % Width);
        int minY = indices.Min(i => i / Width);
        int maxY = indices.Max(i => i / Width);
        return new Vector2((minX + maxX) / 2f, (minY + maxY) / 2f);
    }

    /// <summary>Gets the bounding box of the set for the probe log.</summary>
    /// <returns>Left, top, right and bottom edges, or -1 values for an empty set.</returns>
    public (int MinX, int MinY, int MaxX, int MaxY) Box()
    {
        int[] indices = [.. Enumerable.Range(0, _pixels.Length).Where(i => _pixels[i])];
        return indices.Length == 0
            ? (-1, -1, -1, -1)
            : (indices.Min(i => i % Width), indices.Min(i => i / Width), indices.Max(i => i % Width), indices.Max(i => i / Width));
    }

    /// <summary>Copies a capture as RGBA8 bytes without changing the original.</summary>
    /// <param name="image">Capture.</param>
    /// <returns>Four bytes per pixel.</returns>
    private static byte[] Rgba(Image image)
    {
        using Image copy = (Image)image.Duplicate();
        copy.Convert(Image.Format.Rgba8);
        return copy.GetData();
    }
}
