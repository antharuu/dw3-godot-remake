using System.Buffers.Binary;
using Dw3.Formats.Binary;

namespace Dw3.Formats.Battle;

/// <summary>
/// Decodes EU battle HUD sprite assets:
/// <list type="bullet">
///   <item>Sprite sheet (file 0x455, WFIGHTMN): SpriteDrawer parts + frame records.</item>
///   <item>Texture (file 0x456): cursor bar TIM and RLEN-compressed 4-bit HUD page.</item>
///   <item>Report sprite sheet (file 0x79F) and texture (file 0x7A0): STFGTREP report screen.</item>
/// </list>
/// All colour conversion uses the same formula as the Python reference: value × 255 ÷ 31.
/// </summary>
public static class BattleHudDecoder
{
    // Engine file ids (EU disc).
    public const int HudSheetFileId = 0x455;   // FILE_BATTLE_MENU (WFIGHTMN)
    public const int HudImagesFileId = 0x456;  // FILE_BATTLE_IMAGES (cursor + texture)
    public const int ReportSheetFileId = 0x79F;   // STFGTREP sprites archive (entry 0 = sheet)
    public const int ReportTextureFileId = 0x7A0; // STFGTREP texture archive
    public const int MenuSheetFileId = 0x286;     // FILE_MENU_SPRITES (CMFONTCS)
    public const int MenuTextureFileId = 0x287;   // FILE_FONT (CMFONTTM)

    // 4-bit page dimensions: 64 halfwords × 256 rows = 256×256 texels.
    public const int PageWidthWords = 64;
    public const int PageHeight = 256;

    // Cursor bar (entry 0 of FILE_BATTLE_IMAGES): 12 frames of 12×12, strip 156×12.
    public const int CursorFrames = 12;
    public const int CursorW = 12;
    public const int CursorH = 12;

    // ── sprite sheet parsing ──────────────────────────────────────────────────

    /// <summary>
    /// Parses a SpriteDrawer sprite sheet into parts, frame-id list and frame-offset table.
    /// Layout (body after 4-byte skip): [parts_offset u32][ids_offset u32][frame_offsets…][parts][ids 0xFF-terminated][frames]
    /// The part table may include a 2-byte pad when the part count is odd.
    /// </summary>
    /// <param name="data">Full file bytes (the first 4 bytes are a skip word).</param>
    public static (IReadOnlyList<HudSpritePart> Parts, IReadOnlyList<int> Ids, IReadOnlyList<int> FrameOffsets)
        ParseSheet(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length < 12)
            throw new FormatException("Sprite sheet is truncated.");

        // Sheet body starts 4 bytes in (the leading u32 is an archive-skip pointer).
        ReadOnlySpan<byte> sheet = data.AsSpan(4);
        if (sheet.Length < 8)
            throw new FormatException("Sprite sheet body too short for header.");

        int partsOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(sheet);
        int idsOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(sheet.Slice(4));
        if (partsOffset < 8 || idsOffset <= partsOffset || idsOffset > sheet.Length)
            throw new FormatException("Sprite sheet header offsets are invalid.");

        // Frame-id list: bytes terminated by 0xFF, starting at idsOffset within sheet.
        var ids = new List<int>();
        int cursor = idsOffset;
        while (cursor < sheet.Length && sheet[cursor] != 0xFF)
        {
            ids.Add(sheet[cursor]);
            cursor++;
        }
        if (ids.Count == 0 || cursor >= sheet.Length)
            throw new FormatException("Sprite sheet has no terminated frame-id list.");

        // Parts table: 7 × s16 = 14 bytes each; may have a 2-byte pad before alignment.
        int partTableBytes = idsOffset - partsOffset;
        int remainder = partTableBytes % 14;
        if (remainder is not 0 and not 2)
            throw new FormatException("Sprite sheet part table has an unexpected width.");
        int numParts = (partTableBytes - (remainder == 2 ? 2 : 0)) / 14;
        var parts = new HudSpritePart[numParts];
        for (int i = 0; i < numParts; i++)
        {
            int at = partsOffset + i * 14;
            parts[i] = new HudSpritePart(
                BinaryPrimitives.ReadInt16LittleEndian(sheet.Slice(at)),
                BinaryPrimitives.ReadInt16LittleEndian(sheet.Slice(at + 2)),
                BinaryPrimitives.ReadInt16LittleEndian(sheet.Slice(at + 4)),
                BinaryPrimitives.ReadInt16LittleEndian(sheet.Slice(at + 6)),
                BinaryPrimitives.ReadInt16LittleEndian(sheet.Slice(at + 8)),
                BinaryPrimitives.ReadInt16LittleEndian(sheet.Slice(at + 10)),
                BinaryPrimitives.ReadInt16LittleEndian(sheet.Slice(at + 12)));
        }

        // Frame offsets: 4 bytes each, starting at byte 8 of the sheet body.
        int firstFrameOffset = cursor + 1; // first byte past the 0xFF terminator
        var frameOffsets = new int[ids.Count];
        for (int i = 0; i < ids.Count; i++)
        {
            int off = (int)BinaryPrimitives.ReadUInt32LittleEndian(sheet.Slice(8 + i * 4));
            if (off < firstFrameOffset || off >= sheet.Length)
                throw new FormatException($"Sprite sheet frame offset {i} (0x{off:X}) is out of range.");
            frameOffsets[i] = off;
        }

        return (parts, ids, frameOffsets);
    }

    // ── texture decoding ──────────────────────────────────────────────────────

    /// <summary>
    /// Decodes FILE_BATTLE_IMAGES (file 0x456) entry 1 into one 4-bit texture page.
    /// Entry 0 is the cursor bar; entry 1 is an inner archive containing one RLEN TIM.
    /// </summary>
    public static IReadOnlyList<HudTexturePage> BuildHudTexturePages(byte[] imagesData)
    {
        ArgumentNullException.ThrowIfNull(imagesData);
        if (imagesData.Length < 8)
        {
            throw new FormatException("FILE_BATTLE_IMAGES is too short for two archive entries.");
        }

        // Outer archive: u32[0]=entry0_offset, u32[1]=entry1_offset.
        int entry1Off = (int)BinaryPrimitives.ReadUInt32LittleEndian(imagesData.AsSpan(4));
        if (entry1Off < 8 || entry1Off >= imagesData.Length)
        {
            throw new FormatException("FILE_BATTLE_IMAGES entry 1 offset is out of range.");
        }

        // Entry 1 is itself a one-entry inner archive.
        ReadOnlySpan<byte> inner = imagesData.AsSpan(entry1Off);
        if (inner.Length < 4)
            throw new FormatException("FILE_BATTLE_IMAGES inner archive is too short.");
        int texOff = (int)BinaryPrimitives.ReadUInt32LittleEndian(inner);
        if (texOff < 4 || texOff >= inner.Length)
        {
            throw new FormatException("FILE_BATTLE_IMAGES inner texture offset is out of range.");
        }

        // RLEN-compressed TIM.
        byte[] raw = DecompressRlen(inner.Slice(texOff));
        return [DecodeFourBitTim(raw)];
    }

    /// <summary>
    /// Decodes a multi-entry model-archive texture (used by the report texture file 0x7A0).
    /// The archive uses the same ModelArchive offset-table format as fighter model files.
    /// The input starts with a 4-byte pointer that must equal 4 (one-entry outer wrapper).
    /// </summary>
    public static IReadOnlyList<HudTexturePage> BuildReportTexturePages(byte[] archiveWithSkip)
    {
        ArgumentNullException.ThrowIfNull(archiveWithSkip);
        if (archiveWithSkip.Length < 12)
            throw new FormatException("Report texture file is too short.");
        uint skip = BinaryPrimitives.ReadUInt32LittleEndian(archiveWithSkip.AsSpan());
        if (skip != 4)
        {
            throw new FormatException($"Report texture file outer pointer is {skip}, expected 4.");
        }

        // Strip the leading 4-byte skip and parse the inner archive.
        byte[] archive = archiveWithSkip[4..];
        IReadOnlyList<ArchiveRange> bounds = ModelArchive.ReadBounds(archive.AsSpan());
        var pages = new List<HudTexturePage>();
        for (int i = 0; i < bounds.Count; i++)
        {
            ArchiveRange range = bounds[i];
            if (range.IsEmpty) continue;
            byte[] entry = archive[range.Start..range.End];
            byte[] raw = RlenDecoder.IsRlen(entry) ? RlenDecoder.Decompress(entry) : entry;
            pages.Add(DecodeFourBitTim(raw));
        }
        if (pages.Count == 0)
            throw new FormatException("Report texture archive contains no texture pages.");
        return pages;
    }

    /// <summary>
    /// Decodes the CMFONTTM.BIN texture archive (file 0x287) used by the CMFONTCS menu sheet.
    /// Format: 4-byte skip, then an offset table (u32 values relative to byte 4, terminated by 0),
    /// each pointing to an RLEN-compressed 4-bit TIM page.
    /// </summary>
    public static IReadOnlyList<HudTexturePage> BuildMenuTexturePages(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length < 16)
        {
            throw new FormatException("Menu texture archive is truncated.");
        }

        // Offset table starts at byte 4, entries are relative to byte 4, terminated by 0.
        var offsets = new List<int>();
        for (int index = 0; ; index++)
        {
            int at = 4 + index * 4;
            if (at + 4 > data.Length)
                throw new FormatException("Menu texture archive offset table is truncated.");
            int offset = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at));
            if (offset == 0) break;
            if (offset < index * 4 || 4 + offset >= data.Length)
                throw new FormatException($"Menu texture archive offset {index} (0x{offset:X}) is invalid.");
            offsets.Add(offset);
        }
        if (offsets.Count == 0)
        {
            throw new FormatException("Menu texture archive has no entries.");
        }

        var pages = new List<HudTexturePage>();
        foreach (int offset in offsets)
        {
            byte[] raw = DecompressRlen(data.AsSpan(4 + offset));
            pages.Add(DecodeFourBitTim(raw));
        }
        return pages;
    }

    /// <summary>
    /// Decodes an RLEN-compressed or raw 4-bit TIM (with or without CLUT block) into a texture page.
    /// The image block must be 64 halfwords × 256 rows.
    /// </summary>
    public static HudTexturePage DecodeFourBitTim(byte[] raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        if (raw.Length < 8 || BinaryPrimitives.ReadUInt32LittleEndian(raw) != 0x10)
        {
            throw new FormatException("4-bit TIM magic 0x10 not found.");
        }

        int cursor = 8;
        uint flags = BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(4));
        if ((flags & 8) != 0)
        {
            // Skip the CLUT block (present in 4/8-bit TIMs).
            if (cursor + 4 > raw.Length) throw new FormatException("TIM CLUT block header is truncated.");
            uint clutSize = BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(cursor));
            if (clutSize < 12 || cursor + clutSize > raw.Length) throw new FormatException("TIM CLUT block is invalid.");
            cursor += (int)clutSize;
        }

        if (cursor + 12 > raw.Length) throw new FormatException("TIM image block header is truncated.");
        ushort words = BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(cursor + 8));
        ushort height = BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(cursor + 10));
        if (words != PageWidthWords || height != PageHeight)
            throw new FormatException($"TIM image block is {words}×{height} halfwords, expected {PageWidthWords}×{PageHeight}.");
        uint imgSize = BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(cursor));
        if (imgSize < 12 || cursor + imgSize > raw.Length) throw new FormatException("TIM image block is truncated.");

        int dataStart = cursor + 12;
        var rows = new IReadOnlyList<ushort>[height];
        for (int row = 0; row < height; row++)
        {
            var rowData = new ushort[words];
            for (int w = 0; w < words; w++)
                rowData[w] = BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(dataStart + (row * words + w) * 2));
            rows[row] = rowData;
        }
        return new HudTexturePage(rows);
    }

    // ── cursor bar ───────────────────────────────────────────────────────────

    /// <summary>
    /// Decodes the cursor bar TIM (entry 0 of FILE_BATTLE_IMAGES) into RGBA pixels.
    /// Returns (width, height, rgbaPixels). The strip is 156×12 with 12 frames of 12×12.
    /// The last frame (index 12) is the "off" cursor.
    /// </summary>
    public static (int Width, int Height, byte[] Rgba) DecodeCursorBar(byte[] imagesData)
    {
        ArgumentNullException.ThrowIfNull(imagesData);
        if (imagesData.Length < 8)
        {
            throw new FormatException("FILE_BATTLE_IMAGES is too short.");
        }

        int entry0Off = (int)BinaryPrimitives.ReadUInt32LittleEndian(imagesData.AsSpan());
        int entry1Off = (int)BinaryPrimitives.ReadUInt32LittleEndian(imagesData.AsSpan(4));
        if (entry0Off < 4 || entry1Off <= entry0Off || entry1Off > imagesData.Length)
        {
            throw new FormatException("FILE_BATTLE_IMAGES cursor bar archive offsets are invalid.");
        }

        // Direct-16 TIM: no CLUT, image block starts at byte 8.
        ReadOnlySpan<byte> tim = imagesData.AsSpan(entry0Off, entry1Off - entry0Off);
        if (tim.Length < 20 || BinaryPrimitives.ReadUInt32LittleEndian(tim) != 0x10)
        {
            throw new FormatException("Battle HUD cursor bar is not a direct-16 TIM.");
        }

        ushort words = BinaryPrimitives.ReadUInt16LittleEndian(tim.Slice(16));
        ushort height = BinaryPrimitives.ReadUInt16LittleEndian(tim.Slice(18));
        int pixelCount = words * height;
        if (tim.Length < 20 + pixelCount * 2)
            throw new FormatException("Battle HUD cursor bar TIM pixel data is truncated.");
        if (words < (CursorFrames + 1) * CursorW || height != CursorH)
        {
            throw new FormatException($"Battle HUD cursor strip is {words}×{height}, expected at least {(CursorFrames + 1) * CursorW}×{CursorH}.");
        }

        var rgba = new byte[pixelCount * 4];
        for (int i = 0; i < pixelCount; i++)
        {
            ushort word = BinaryPrimitives.ReadUInt16LittleEndian(tim.Slice(20 + i * 2));
            if (word == 0) continue; // transparent
            int at = i * 4;
            rgba[at] = Ps1Channel(word & 31);
            rgba[at + 1] = Ps1Channel((word >> 5) & 31);
            rgba[at + 2] = Ps1Channel((word >> 10) & 31);
            rgba[at + 3] = 255;
        }
        return (words, height, rgba);
    }

    // ── frame rendering ───────────────────────────────────────────────────────

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
        RenderFrame(IReadOnlyList<HudSpritePart> parts, ReadOnlySpan<byte> sheetBody,
            int frameOffset, IReadOnlyList<HudTexturePage> pages, int? clutRowOverride = null)
    {
        ArgumentNullException.ThrowIfNull(parts);
        ArgumentNullException.ThrowIfNull(pages);
        if (frameOffset < 0 || frameOffset + 6 > sheetBody.Length)
        {
            throw new FormatException("Sprite frame offset is out of range.");
        }

        short count = BinaryPrimitives.ReadInt16LittleEndian(sheetBody.Slice(frameOffset));
        short frameRow = BinaryPrimitives.ReadInt16LittleEndian(sheetBody.Slice(frameOffset + 2));
        short abr = BinaryPrimitives.ReadInt16LittleEndian(sheetBody.Slice(frameOffset + 4));
        if (count <= 0 || count > 64)
        {
            throw new FormatException($"Sprite frame has invalid part count {count}.");
        }

        var rendered = new (int Dx, int Dy, int W, int H, byte[] Rgba)[count];
        for (int i = 0; i < count; i++)
        {
            int at = frameOffset + 6 + i * 6;
            if (at + 6 > sheetBody.Length) throw new FormatException("Sprite frame part record is out of range.");
            short partIdx = BinaryPrimitives.ReadInt16LittleEndian(sheetBody.Slice(at));
            short dx = BinaryPrimitives.ReadInt16LittleEndian(sheetBody.Slice(at + 2));
            short dy = BinaryPrimitives.ReadInt16LittleEndian(sheetBody.Slice(at + 4));
            if (partIdx < 0 || partIdx >= parts.Count)
                throw new FormatException($"Sprite frame references missing part {partIdx}.");
            HudSpritePart part = parts[partIdx];
            if (part.Mode != 0 || part.Width <= 0 || part.Height <= 0)
            {
                throw new FormatException("Sprite part bounds are invalid.");
            }

            int clutY = part.ClutY + frameRow + (clutRowOverride ?? 0);
            ushort[] palette = ReadPalette(pages, part.ClutX, clutY, abr != 0);
            byte[] partRgba = RenderPart(pages, part, palette, abr != 0);
            rendered[i] = (dx, dy, part.Width, part.Height, partRgba);
        }

        int minX = rendered.Min(r => r.Dx);
        int minY = rendered.Min(r => r.Dy);
        int maxX = rendered.Max(r => r.Dx + r.W);
        int maxY = rendered.Max(r => r.Dy + r.H);
        int w = maxX - minX, h = maxY - minY;
        var result = new byte[w * h * 4];

        // SpriteDrawer draws backward (last part is drawn first = deepest).
        for (int i = rendered.Length - 1; i >= 0; i--)
        {
            var (rdx, rdy, rw, rh, rgba) = rendered[i];
            AlphaComposite(result, w, h, rgba, rw, rh, rdx - minX, rdy - minY);
        }
        return (result, w, h, minX, minY, abr);
    }

    /// <summary>
    /// Renders one sprite frame and also collects per-part metadata (part index, dx, dy).
    /// Used by the report decoder which needs both the image and the metadata.
    /// </summary>
    public static (byte[] Rgba, int Width, int Height, int OffsetX, int OffsetY, int Abr,
        IReadOnlyList<(int Part, int Dx, int Dy)> PartMeta)
        RenderFrameWithMeta(IReadOnlyList<HudSpritePart> parts, ReadOnlySpan<byte> sheetBody,
            int frameOffset, IReadOnlyList<HudTexturePage> pages, int? clutRowOverride = null)
    {
        // Collect part meta first (doesn't depend on pixel data).
        if (frameOffset < 0 || frameOffset + 6 > sheetBody.Length)
            throw new FormatException("Sprite frame offset is out of range.");
        short count = BinaryPrimitives.ReadInt16LittleEndian(sheetBody.Slice(frameOffset));
        if (count <= 0 || count > 64) throw new FormatException($"Sprite frame has invalid part count {count}.");
        var meta = new (int Part, int Dx, int Dy)[count];
        for (int i = 0; i < count; i++)
        {
            int at = frameOffset + 6 + i * 6;
            meta[i] = (
                BinaryPrimitives.ReadInt16LittleEndian(sheetBody.Slice(at)),
                BinaryPrimitives.ReadInt16LittleEndian(sheetBody.Slice(at + 2)),
                BinaryPrimitives.ReadInt16LittleEndian(sheetBody.Slice(at + 4)));
        }
        var (rgba, w, h, ox, oy, abr) = RenderFrame(parts, sheetBody, frameOffset, pages, clutRowOverride);
        return (rgba, w, h, ox, oy, abr, meta);
    }

    /// <summary>
    /// Renders one sprite frame using field_frame semantics (metadata includes u/v/w/h/dx/dy/semi/clutRow).
    /// Used by the report decoder for the arrow frame from CMFONTCS.
    /// </summary>
    public static (byte[] Rgba, int Width, int Height, IReadOnlyList<FieldFramePartMeta> PartMeta, int Abr)
        RenderFieldFrame(IReadOnlyList<HudSpritePart> parts, ReadOnlySpan<byte> sheetBody,
            int frameOffset, IReadOnlyList<HudTexturePage> pages, int clutRow = 0)
    {
        if (frameOffset < 0 || frameOffset + 6 > sheetBody.Length)
            throw new FormatException("Sprite frame offset is out of range.");
        short count = BinaryPrimitives.ReadInt16LittleEndian(sheetBody.Slice(frameOffset));
        short frameRow = BinaryPrimitives.ReadInt16LittleEndian(sheetBody.Slice(frameOffset + 2));
        short abr = BinaryPrimitives.ReadInt16LittleEndian(sheetBody.Slice(frameOffset + 4));
        if (count <= 0 || count > 64) throw new FormatException($"Sprite frame has invalid part count {count}.");

        var rendered = new (int Dx, int Dy, int W, int H, byte[] Rgba)[count];
        var metadata = new FieldFramePartMeta[count];
        for (int i = 0; i < count; i++)
        {
            int at = frameOffset + 6 + i * 6;
            if (at + 6 > sheetBody.Length) throw new FormatException("Sprite part record is out of range.");
            short partIdx = BinaryPrimitives.ReadInt16LittleEndian(sheetBody.Slice(at));
            short dx = BinaryPrimitives.ReadInt16LittleEndian(sheetBody.Slice(at + 2));
            short dy = BinaryPrimitives.ReadInt16LittleEndian(sheetBody.Slice(at + 4));
            if (partIdx < 0 || partIdx >= parts.Count)
                throw new FormatException($"Sprite frame references missing part {partIdx}.");
            HudSpritePart part = parts[partIdx];
            if (part.Mode != 0 || part.Width <= 0 || part.Height <= 0)
                throw new FormatException("Sprite part bounds are invalid.");
            ushort[] palette = ReadPalette(pages, part.ClutX, part.ClutY + frameRow + clutRow, abr != 0);
            byte[] partRgba = RenderPart(pages, part, palette, abr != 0);
            rendered[i] = (dx, dy, part.Width, part.Height, partRgba);
            metadata[i] = new FieldFramePartMeta(part.U, part.V, part.Width, part.Height, dx, dy,
                abr >= 0, part.ClutY + frameRow);
        }

        int minX = rendered.Min(r => r.Dx);
        int minY = rendered.Min(r => r.Dy);
        int maxX = rendered.Max(r => r.Dx + r.W);
        int maxY = rendered.Max(r => r.Dy + r.H);
        var result = new byte[(maxX - minX) * (maxY - minY) * 4];
        for (int i = rendered.Length - 1; i >= 0; i--)
        {
            var (rdx, rdy, rw, rh, rgba) = rendered[i];
            AlphaComposite(result, maxX - minX, maxY - minY, rgba, rw, rh, rdx - minX, rdy - minY);
        }
        return (result, maxX - minX, result.Length / (4 * Math.Max(1, maxX - minX)), metadata, abr);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads a 16-entry 4-bit palette from texture pages.
    /// page = clutX / PageWidthWords, word = clutX % PageWidthWords within the page row.
    /// Throws with message containing "palette outside loaded texture" when clutY is out of range.
    /// </summary>
    public static ushort[] ReadPalette(IReadOnlyList<HudTexturePage> pages, int clutX, int clutY, bool abr)
    {
        _ = abr;
        int page = clutX / PageWidthWords;
        int word = clutX % PageWidthWords;
        if (page >= pages.Count || clutY < 0 || clutY >= PageHeight)
            throw new FormatException("Sprite palette outside loaded texture.");
        IReadOnlyList<ushort> row = pages[page].Rows[clutY];
        if (word + 16 > row.Count)
            throw new FormatException("Sprite palette row is truncated.");
        var palette = new ushort[16];
        for (int i = 0; i < 16; i++) palette[i] = row[word + i];
        return palette;
    }

    private static byte[] RenderPart(IReadOnlyList<HudTexturePage> pages, HudSpritePart part, ushort[] palette, bool abr)
    {
        int pageIndex = part.U / 256;
        int localU = part.U % 256;
        if (pageIndex >= pages.Count || localU + part.Width > 256 || part.V + part.Height > PageHeight)
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
                ushort hw = row[texel / 4];
                int nibble = (texel % 4) * 4;
                int index = (hw >> nibble) & 0xF;
                ushort color = palette[index];
                if (color != 0)
                {
                    int at = pixel * 4;
                    rgba[at] = Ps1Channel(color & 31);
                    rgba[at + 1] = Ps1Channel((color >> 5) & 31);
                    rgba[at + 2] = Ps1Channel((color >> 10) & 31);
                    // Python: alpha = 128 if abr and word & 0x8000 else 255
                    rgba[at + 3] = (byte)(abr && (color & 0x8000) != 0 ? 128 : 255);
                }
                pixel++;
            }
        }
        return rgba;
    }

    // PS1 5-bit channel expansion matching battle_hud.py: value * 255 // 31 (integer division).
    public static byte Ps1Channel(int value) => (byte)(value * 255 / 31);

    private static void AlphaComposite(byte[] dst, int dstW, int dstH, byte[] src, int srcW, int srcH, int x, int y)
    {
        for (int sy = 0; sy < srcH; sy++)
        {
            int dy = y + sy;
            if (dy < 0 || dy >= dstH) continue;
            for (int sx = 0; sx < srcW; sx++)
            {
                int dx = x + sx;
                if (dx < 0 || dx >= dstW) continue;
                int dstAt = (dy * dstW + dx) * 4;
                int srcAt = (sy * srcW + sx) * 4;
                byte sa = src[srcAt + 3];
                if (sa == 0) continue;
                if (sa == 255 || dst[dstAt + 3] == 0)
                {
                    dst[dstAt] = src[srcAt]; dst[dstAt + 1] = src[srcAt + 1];
                    dst[dstAt + 2] = src[srcAt + 2]; dst[dstAt + 3] = sa;
                }
                else
                {
                    // PIL alpha_composite: dst = src + dst*(1-alpha/255)
                    float alpha = sa / 255f;
                    float inv = 1f - alpha;
                    float da = dst[dstAt + 3] / 255f;
                    float outA = alpha + da * inv;
                    if (outA > 0)
                    {
                        dst[dstAt] = (byte)MathF.Round((src[srcAt] * alpha + dst[dstAt] * da * inv) / outA);
                        dst[dstAt + 1] = (byte)MathF.Round((src[srcAt + 1] * alpha + dst[dstAt + 1] * da * inv) / outA);
                        dst[dstAt + 2] = (byte)MathF.Round((src[srcAt + 2] * alpha + dst[dstAt + 2] * da * inv) / outA);
                        dst[dstAt + 3] = (byte)MathF.Round(outA * 255);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Decompresses an RLEN block following the battle_hud.py _rlen semantics exactly:
    /// only 0x00 is end-of-stream; 0x80 is a 0-count repeat (reads and discards value byte).
    /// </summary>
    public static byte[] DecompressRlen(ReadOnlySpan<byte> data)
    {
        if (data.Length < 9 || data[0] != 'R' || data[1] != 'L' || data[2] != 'E' || data[3] != 'N')
            throw new FormatException("Expected RLEN-compressed data.");
        uint target = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(4));
        var output = new byte[target];
        int pos = 8, outPos = 0;
        while (true)
        {
            if (pos >= data.Length) throw new FormatException("RLEN stream lacks end marker.");
            byte cmd = data[pos++];
            if (cmd == 0) break;
            if ((cmd & 0x80) != 0)
            {
                int cnt = cmd & 0x7F;
                if (pos >= data.Length) throw new FormatException("RLEN repeat command has no value byte.");
                byte val = data[pos++];
                if (outPos + cnt > output.Length) throw new FormatException("RLEN exceeds declared size.");
                for (int i = 0; i < cnt; i++) output[outPos++] = val;
            }
            else
            {
                if (pos + cmd > data.Length) throw new FormatException("RLEN literal run exceeds input.");
                if (outPos + cmd > output.Length) throw new FormatException("RLEN exceeds declared size.");
                data.Slice(pos, cmd).CopyTo(output.AsSpan(outPos));
                pos += cmd; outPos += cmd;
            }
        }
        if (outPos != (int)target)
            throw new FormatException($"RLEN decompressed {outPos} bytes, expected {target}.");
        return output;
    }
}
