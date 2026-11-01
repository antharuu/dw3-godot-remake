using Dw3.Formats.Binary.Tim;

namespace Dw3.Formats.Water;

/// <summary>
/// One composed sprite frame represented as raw 16-bit PS1 colour words.
/// </summary>
/// <param name="Width">Width of the frame bounding box.</param>
/// <param name="Height">Height of the frame bounding box.</param>
/// <param name="OffsetX">X offset of the bounding box from the object origin.</param>
/// <param name="OffsetY">Y offset of the bounding box from the object origin.</param>
/// <param name="Abr">PS1 semi-transparency blend mode, or a negative value when absent.</param>
/// <param name="Words">Row-major PS1 colour words, with zero denoting transparency.</param>
public sealed record SpriteFrameImage(int Width, int Height, int OffsetX, int OffsetY, int Abr, ushort[] Words)
{
    /// <summary>
    /// Renders only opaque, non-STP texels as RGBA.
    /// </summary>
    /// <returns>RGBA bytes with all omitted texels fully transparent.</returns>
    public byte[] ToOpaqueRgba() => Select(stp: false);

    /// <summary>
    /// Renders only STP texels as opaque RGBA.
    /// </summary>
    /// <returns>RGBA bytes with all omitted texels fully transparent.</returns>
    public byte[] ToStpRgba() => Select(stp: true);

    private byte[] Select(bool stp)
    {
        var rgba = new byte[Words.Length * 4];
        for (int index = 0; index < Words.Length; index++)
        {
            ushort word = Words[index];
            if (word == 0 || Rgba5551.IsStp(word) != stp)
            {
                continue;
            }

            (byte r, byte g, byte b, _) = Rgba5551.ToRgba(word);
            rgba[index * 4] = r;
            rgba[index * 4 + 1] = g;
            rgba[index * 4 + 2] = b;
            rgba[index * 4 + 3] = byte.MaxValue;
        }

        return rgba;
    }
}
