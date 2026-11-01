using System.Buffers.Binary;
using Dw3.Formats.Binary.Tim;

namespace Dw3.Formats.Water;

/// <summary>
/// One composed sprite frame as raw 16-bit PS1 colour words. Word 0 is a transparent
/// texel; a non-zero word with bit 15 set is a semi-transparent (STP) texel.
/// </summary>
/// <param name="Width">Width of the frame bounding box.</param>
/// <param name="Height">Height of the frame bounding box.</param>
/// <param name="OffsetX">X of the bounding box relative to the object position.</param>
/// <param name="OffsetY">Y of the bounding box relative to the object position.</param>
/// <param name="Abr">Semi-transparency blend mode recorded by the frame, or a negative value.</param>
/// <param name="Words">Row-major colour words.</param>
public sealed record SpriteFrameImage(int Width, int Height, int OffsetX, int OffsetY, int Abr, ushort[] Words)
{
    /// <summary>RGBA of the opaque (non-STP) texels; every other texel is fully transparent.</summary>
    public byte[] ToOpaqueRgba() => Select(stp: false);

    /// <summary>RGBA of the STP texels drawn fully opaque; every other texel is fully transparent.</summary>
    public byte[] ToStpRgba() => Select(stp: true);

    private byte[] Select(bool stp)
    {
        var rgba = new byte[Words.Length * 4];
        for (int index = 0; index < Words.Length; index++)
        {
            ushort word = Words[index];
            if (word == 0 || Rgba5551.IsStp(word) != stp)
                continue;
            (byte r, byte g, byte b, _) = Rgba5551.ToRgba(word);
            rgba[index * 4] = r;
            rgba[index * 4 + 1] = g;
            rgba[index * 4 + 2] = b;
            rgba[index * 4 + 3] = byte.MaxValue;
        }
        return rgba;
    }
}

/// <summary>
/// Composes a sprite-sheet frame from its parts sampled in the object VRAM.
///
/// Frame layout (s16 little-endian): part count (1..16), palette row shift, blend mode,
/// then per part: part index, x, y. Parts are emitted in reverse order, so the first
/// listed part ends on top; transparent texels never overwrite.
///
/// 4-bit parts read their 16-colour palette from VRAM at
/// (0x140 + ClutX, 0x100 + ClutY + row shift + palette row), inside the loaded pages.
/// 8-bit parts read the alternate palette row ClutY + row shift + palette row - 0x100.
/// </summary>
public static class SpriteFrameRenderer
{
    private const int MaximumParts = 16;
    private const int FourBitPageTexels = 256;
    private const int EightBitPageTexels = 128;
    private const int VramPaletteOriginX = 0x140;
    private const int VramPaletteOriginY = 0x100;
    private const int VramHeight = 0x200;
    private const int FourBitPaletteSize = 16;

    /// <summary>Renders the frame with the palette shifted by <paramref name="clutRow"/>.</summary>
    /// <exception cref="FormatException">The frame or one of its parts references data outside the loaded VRAM.</exception>
    public static SpriteFrameImage Render(IReadOnlyList<SpritePart> parts, ReadOnlySpan<byte> frame, ObjectVram vram, int clutRow)
    {
        if (frame.Length < 6)
            throw new FormatException("sprite frame truncated");
        int count = BinaryPrimitives.ReadInt16LittleEndian(frame);
        int frameRow = BinaryPrimitives.ReadInt16LittleEndian(frame[2..]);
        int abr = BinaryPrimitives.ReadInt16LittleEndian(frame[4..]);
        if (count <= 0 || count > MaximumParts)
            throw new FormatException("sprite frame part count invalid");
        if (frame.Length < 6 + 6 * count)
        {
            throw new FormatException("sprite frame truncated");
        }

        var placed = new (int X, int Y, int Width, int Height, ushort[] Words)[count];
        for (int index = 0; index < count; index++)
        {
            int partIndex = BinaryPrimitives.ReadInt16LittleEndian(frame[(6 + 6 * index)..]);
            int x = BinaryPrimitives.ReadInt16LittleEndian(frame[(8 + 6 * index)..]);
            int y = BinaryPrimitives.ReadInt16LittleEndian(frame[(10 + 6 * index)..]);
            if (partIndex < 0 || partIndex >= parts.Count)
                throw new FormatException("sprite frame references missing part");
            SpritePart part = parts[partIndex];
            placed[index] = (x, y, part.Width, part.Height, Sample(part, vram, frameRow + clutRow));
        }

        int minX = placed.Min(item => item.X);
        int minY = placed.Min(item => item.Y);
        int width = placed.Max(item => item.X + item.Width) - minX;
        int height = placed.Max(item => item.Y + item.Height) - minY;
        var words = new ushort[width * height];
        for (int index = count - 1; index >= 0; index--)
        {
            var (x, y, partWidth, partHeight, partWords) = placed[index];
            for (int row = 0; row < partHeight; row++)
                for (int column = 0; column < partWidth; column++)
                {
                    ushort word = partWords[row * partWidth + column];
                    if (word != 0)
                        words[(y - minY + row) * width + x - minX + column] = word;
                }
        }
        return new SpriteFrameImage(width, height, minX, minY, abr, words);
    }

    private static ushort[] Sample(SpritePart part, ObjectVram vram, int rowShift)
    {
        if (Math.Min(Math.Min(part.U, part.V), Math.Min(Math.Min(part.Width, part.Height), Math.Min(part.ClutX, part.ClutY))) < 0 ||
            part.Width <= 0 || part.Height <= 0 || part.V + part.Height > ObjectTextureArchiveDecoder.PageRows)
            throw new FormatException("sprite part bounds invalid");
        return part.Mode switch
        {
            0 => SampleFourBit(part, vram, rowShift),
            1 => SampleEightBit(part, vram, rowShift),
            _ => throw new FormatException("unsupported sprite texture mode"),
        };
    }

    private static ushort[] SampleFourBit(SpritePart part, ObjectVram vram, int rowShift)
    {
        int page = part.U / FourBitPageTexels;
        int localU = part.U % FourBitPageTexels;
        if (page >= vram.Pages.Count || localU + part.Width > FourBitPageTexels)
            throw new FormatException("4-bit sprite references unavailable VRAM page");
        int paletteRow = part.ClutY + rowShift;
        int paletteX = VramPaletteOriginX + part.ClutX;
        int paletteWord = part.ClutX % ObjectTextureArchiveDecoder.PageWords;
        if (paletteRow < 0 || VramPaletteOriginY + paletteRow >= VramHeight ||
            paletteX + FourBitPaletteSize > VramPaletteOriginX + vram.Pages.Count * ObjectTextureArchiveDecoder.PageWords ||
            paletteWord + FourBitPaletteSize > ObjectTextureArchiveDecoder.PageWords)
            throw new FormatException("4-bit sprite palette outside loaded VRAM");
        ushort[] palettePage = vram.Pages[part.ClutX / ObjectTextureArchiveDecoder.PageWords];
        ReadOnlySpan<ushort> palette = palettePage.AsSpan(paletteRow * ObjectTextureArchiveDecoder.PageWords + paletteWord, FourBitPaletteSize);

        ushort[] texels = vram.Pages[page];
        var words = new ushort[part.Width * part.Height];
        for (int row = 0; row < part.Height; row++)
            for (int column = 0; column < part.Width; column++)
            {
                int u = localU + column;
                ushort packed = texels[(part.V + row) * ObjectTextureArchiveDecoder.PageWords + u / 4];
                words[row * part.Width + column] = palette[(packed >> ((u & 3) * 4)) & 15];
            }
        return words;
    }

    private static ushort[] SampleEightBit(SpritePart part, ObjectVram vram, int rowShift)
    {
        int page = part.U / EightBitPageTexels;
        int localU = part.U % EightBitPageTexels;
        int paletteRow = part.ClutY + rowShift - VramPaletteOriginY;
        if (page >= vram.Pages.Count || localU + part.Width > EightBitPageTexels || paletteRow < 0 ||
            paletteRow >= vram.AlternatePalettes.Count || part.ClutX + 256 > vram.AlternatePalettes[paletteRow].Length)
            throw new FormatException("8-bit sprite bounds or alternate CLUT invalid");
        ushort[] palette = vram.AlternatePalettes[paletteRow];

        ushort[] texels = vram.Pages[page];
        var words = new ushort[part.Width * part.Height];
        for (int row = 0; row < part.Height; row++)
            for (int column = 0; column < part.Width; column++)
            {
                int u = localU + column;
                ushort packed = texels[(part.V + row) * ObjectTextureArchiveDecoder.PageWords + u / 2];
                words[row * part.Width + column] = palette[part.ClutX + ((packed >> (8 * (u & 1))) & 0xFF)];
            }
        return words;
    }
}
