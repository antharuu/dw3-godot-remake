using System.Buffers.Binary;

namespace Dw3.Formats.Battle;

/// <summary>
/// Renders SpriteDrawer frames (battle HUD, result screen, menu and talk-box sheets) from their
/// part table and 4-bit texture pages into RGBA images.
/// </summary>
/// <remarks>
/// A frame record is a <see cref="HudFrameHeader"/> followed by one 6-byte record per part:
/// s16 part index into the sheet's part table, s16 dx, s16 dy. Each part samples a 4-bit
/// rectangle of a texture page (page = U / 256) through a 16-colour palette read from the
/// texture pages at (ClutX, ClutY + frame row + override).
/// </remarks>
public static class HudFrameRenderer
{
    private const int PaletteSize = 16;
    private const int PageTexels = 256;

    /// <summary>
    /// Renders one sprite frame from a SpriteDrawer sheet using one or more 4-bit texture pages.
    /// Returns (rgbaPixels, width, height, offsetX, offsetY, abr).
    /// </summary>
    /// <param name="parts">Parsed parts table.</param>
    /// <param name="sheetBody">Sheet body bytes (starting 4 bytes into the raw file).</param>
    /// <param name="frameOffset">Offset into sheetBody of the frame record.</param>
    /// <param name="pages">4-bit texture pages (one or more).</param>
    /// <param name="clutRowOverride">If not null, overrides the per-frame CLUT row for palette animation.</param>
    /// <exception cref="FormatException">With message containing "palette outside loaded texture"
    /// when the computed CLUT row falls outside the page — caller should retry with row 0.</exception>
    public static (byte[] Rgba, int Width, int Height, int OffsetX, int OffsetY, int Abr)
        RenderFrame(
            IReadOnlyList<HudSpritePart> parts,
            ReadOnlySpan<byte> sheetBody,
            int frameOffset,
            IReadOnlyList<HudTexturePage> pages,
            int? clutRowOverride = null)
    {
        ArgumentNullException.ThrowIfNull(parts);
        ArgumentNullException.ThrowIfNull(pages);
        HudFrameHeader header = HudFrameHeader.Read(sheetBody, frameOffset);
        var rendered = new HudRenderedPart[header.Count];
        for (int i = 0; i < header.Count; i++)
        {
            (HudSpritePart part, short dx, short dy) = ReadPart(sheetBody, frameOffset, i, parts, "Sprite frame part record is out of range.");
            rendered[i] = RenderPlacedPart(pages, part, header, clutRowOverride ?? 0, (dx, dy));
        }

        (HudRgbaImage image, int minX, int minY) = HudAlphaCompositor.ComposeBackToFront(rendered);
        return (image.Pixels, image.Width, image.Height, minX, minY, header.Abr);
    }

    /// <summary>
    /// Renders one sprite frame and also collects per-part metadata (part index, dx, dy).
    /// Used by the report decoder which needs both the image and the metadata.
    /// </summary>
    public static (byte[] Rgba, int Width, int Height, int OffsetX, int OffsetY, int Abr,
        IReadOnlyList<(int Part, int Dx, int Dy)> PartMeta)
        RenderFrameWithMeta(
            IReadOnlyList<HudSpritePart> parts,
            ReadOnlySpan<byte> sheetBody,
            int frameOffset,
            IReadOnlyList<HudTexturePage> pages,
            int? clutRowOverride = null)
    {
        // Collect part meta first (doesn't depend on pixel data).
        HudFrameHeader header = HudFrameHeader.Read(sheetBody, frameOffset);
        var meta = new (int Part, int Dx, int Dy)[header.Count];
        for (int i = 0; i < header.Count; i++)
        {
            ReadOnlySpan<byte> record = sheetBody.Slice(PartRecordOffset(frameOffset, i));
            meta[i] = (
                BinaryPrimitives.ReadInt16LittleEndian(record),
                BinaryPrimitives.ReadInt16LittleEndian(record.Slice(2)),
                BinaryPrimitives.ReadInt16LittleEndian(record.Slice(4)));
        }

        var (rgba, w, h, ox, oy, abr) = RenderFrame(parts, sheetBody, frameOffset, pages, clutRowOverride);
        return (rgba, w, h, ox, oy, abr, meta);
    }

    /// <summary>
    /// Renders one sprite frame using field_frame semantics (metadata includes u/v/w/h/dx/dy/semi/clutRow).
    /// Used by the report decoder for the arrow frame from CMFONTCS.
    /// </summary>
    public static (byte[] Rgba, int Width, int Height, IReadOnlyList<FieldFramePartMeta> PartMeta, int Abr)
        RenderFieldFrame(
            IReadOnlyList<HudSpritePart> parts,
            ReadOnlySpan<byte> sheetBody,
            int frameOffset,
            IReadOnlyList<HudTexturePage> pages,
            int clutRow = 0)
    {
        HudFrameHeader header = HudFrameHeader.Read(sheetBody, frameOffset);
        var rendered = new HudRenderedPart[header.Count];
        var metadata = new FieldFramePartMeta[header.Count];
        for (int i = 0; i < header.Count; i++)
        {
            (HudSpritePart part, short dx, short dy) = ReadPart(sheetBody, frameOffset, i, parts, "Sprite part record is out of range.");
            rendered[i] = RenderPlacedPart(pages, part, header, clutRow, (dx, dy));
            metadata[i] = new FieldFramePartMeta(
                part.U, part.V, part.Width, part.Height, dx, dy, header.Abr >= 0, part.ClutY + header.Row);
        }

        (HudRgbaImage image, _, _) = HudAlphaCompositor.ComposeBackToFront(rendered);
        return (image.Pixels, image.Width, image.Height, metadata, header.Abr);
    }

    /// <summary>
    /// Reads a 16-entry 4-bit palette from texture pages.
    /// page = clutX / PageWidthWords, word = clutX % PageWidthWords within the page row.
    /// Throws with message containing "palette outside loaded texture" when clutY is out of range.
    /// </summary>
    public static ushort[] ReadPalette(IReadOnlyList<HudTexturePage> pages, int clutX, int clutY, bool abr)
    {
        _ = abr;
        int page = clutX / HudTexturePage.PageWidthWords;
        int word = clutX % HudTexturePage.PageWidthWords;
        if (page >= pages.Count || clutY < 0 || clutY >= HudTexturePage.PageHeight)
        {
            throw new FormatException("Sprite palette outside loaded texture.");
        }

        IReadOnlyList<ushort> row = pages[page].Rows[clutY];
        if (word + PaletteSize > row.Count)
        {
            throw new FormatException("Sprite palette row is truncated.");
        }

        var palette = new ushort[PaletteSize];
        for (int i = 0; i < PaletteSize; i++)
        {
            palette[i] = row[word + i];
        }

        return palette;
    }

    /// <summary>Byte offset of part record <paramref name="index"/> of the frame at <paramref name="frameOffset"/>.</summary>
    /// <param name="frameOffset">Offset of the frame header in the sheet body.</param>
    /// <param name="index">Zero-based part record index.</param>
    /// <returns>Offset of the record, just after the 6-byte header.</returns>
    private static int PartRecordOffset(int frameOffset, int index)
        => frameOffset + HudFrameHeader.RecordBytes + (index * HudFrameHeader.RecordBytes);

    /// <summary>Reads part record <paramref name="index"/> and resolves it against the part table.</summary>
    /// <param name="sheetBody">Sheet body bytes.</param>
    /// <param name="frameOffset">Offset of the frame header.</param>
    /// <param name="index">Zero-based part record index.</param>
    /// <param name="parts">Parsed part table.</param>
    /// <param name="outOfRangeMessage">Error text used when the record runs past the body.</param>
    /// <returns>The referenced part and its frame-relative offset.</returns>
    /// <exception cref="FormatException">The record overruns the body, names a missing part, or the part is not a positive-size 4-bit part.</exception>
    private static (HudSpritePart Part, short Dx, short Dy) ReadPart(
        ReadOnlySpan<byte> sheetBody, int frameOffset, int index, IReadOnlyList<HudSpritePart> parts, string outOfRangeMessage)
    {
        int at = PartRecordOffset(frameOffset, index);
        if (at + HudFrameHeader.RecordBytes > sheetBody.Length)
        {
            throw new FormatException(outOfRangeMessage);
        }

        short partIdx = BinaryPrimitives.ReadInt16LittleEndian(sheetBody.Slice(at));
        short dx = BinaryPrimitives.ReadInt16LittleEndian(sheetBody.Slice(at + 2));
        short dy = BinaryPrimitives.ReadInt16LittleEndian(sheetBody.Slice(at + 4));
        if (partIdx < 0 || partIdx >= parts.Count)
        {
            throw new FormatException($"Sprite frame references missing part {partIdx}.");
        }

        HudSpritePart part = parts[partIdx];
        if (part.Mode != 0 || part.Width <= 0 || part.Height <= 0)
        {
            throw new FormatException("Sprite part bounds are invalid.");
        }

        return (part, dx, dy);
    }

    /// <summary>Renders <paramref name="part"/> with its palette and places it at <paramref name="offset"/>.</summary>
    /// <param name="pages">Loaded 4-bit texture pages.</param>
    /// <param name="part">Validated part.</param>
    /// <param name="header">Frame header supplying the CLUT row and blend mode.</param>
    /// <param name="extraRow">Additional CLUT row (palette animation override, 0 for none).</param>
    /// <param name="offset">Frame-relative position of the part.</param>
    /// <returns>The rendered, positioned part.</returns>
    private static HudRenderedPart RenderPlacedPart(
        IReadOnlyList<HudTexturePage> pages, HudSpritePart part, HudFrameHeader header, int extraRow, (int Dx, int Dy) offset)
    {
        bool semiTransparent = header.Abr != 0;
        ushort[] palette = ReadPalette(pages, part.ClutX, part.ClutY + header.Row + extraRow, semiTransparent);
        byte[] partRgba = RenderPart(pages, part, palette, semiTransparent);
        return new HudRenderedPart(offset.Dx, offset.Dy, new HudRgbaImage(partRgba, part.Width, part.Height));
    }

    /// <summary>Samples the 4-bit texels of <paramref name="part"/> through <paramref name="palette"/>.</summary>
    /// <param name="pages">Loaded 4-bit texture pages.</param>
    /// <param name="part">Part rectangle; U selects the page (U / 256) and the texel column within it.</param>
    /// <param name="palette">16 PS1 colour words; colour 0 is transparent.</param>
    /// <param name="abr">When set, colours with the STP bit are drawn at alpha 128 instead of 255.</param>
    /// <returns>RGBA bytes of the part.</returns>
    /// <exception cref="FormatException">The rectangle exceeds the loaded pages.</exception>
    private static byte[] RenderPart(IReadOnlyList<HudTexturePage> pages, HudSpritePart part, ushort[] palette, bool abr)
    {
        int pageIndex = part.U / PageTexels;
        int localU = part.U % PageTexels;
        if (pageIndex >= pages.Count || localU + part.Width > PageTexels || part.V + part.Height > HudTexturePage.PageHeight)
        {
            throw new FormatException("Sprite part exceeds loaded texture page bounds.");
        }

        var rgba = new byte[part.Width * part.Height * 4];
        int pixel = 0;
        IReadOnlyList<IReadOnlyList<ushort>> rows = pages[pageIndex].Rows;
        for (int py = part.V; py < part.V + part.Height; py++)
        {
            IReadOnlyList<ushort> row = rows[py];
            for (int px = 0; px < part.Width; px++)
            {
                int texel = localU + px;
                int index = (row[texel / 4] >> ((texel % 4) * 4)) & 0xF;
                WritePaletteColor(rgba, pixel * 4, palette[index], abr);
                pixel++;
            }
        }

        return rgba;
    }

    /// <summary>Writes one palette colour; colour word 0 leaves the pixel transparent.</summary>
    /// <param name="rgba">Destination RGBA buffer.</param>
    /// <param name="at">Byte offset of the pixel.</param>
    /// <param name="color">PS1 colour word.</param>
    /// <param name="abr">When set, the STP bit (0x8000) lowers alpha to 128.</param>
    private static void WritePaletteColor(byte[] rgba, int at, ushort color, bool abr)
    {
        if (color == 0)
        {
            return;
        }

        HudColor.WriteRgb(rgba, at, color);

        // Python: alpha = 128 if abr and word & 0x8000 else 255
        rgba[at + 3] = (byte)(abr && (color & 0x8000) != 0 ? 128 : 255);
    }
}
