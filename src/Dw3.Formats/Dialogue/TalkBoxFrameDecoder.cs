using System.Buffers.Binary;
using Dw3.Formats.Battle;

namespace Dw3.Formats.Dialogue;

/// <summary>
/// Decodes the field talk box: its corner, edge and arrow sprites from the menu sprite sheet
/// (CMFONTCS.BIN) drawn with the 4-bit menu texture (CMFONTTM.BIN), the semi-transparent
/// panel sampled from the same texture, and the executable's four-entry layout table.
/// </summary>
public static class TalkBoxFrameDecoder
{
    /// <summary>Number of talk-box types, one layout record each.</summary>
    public const int LayoutCount = 4;

    /// <summary>Bytes per layout record: one s32 sprite id then fourteen s16 coordinates.</summary>
    public const int LayoutRecordBytes = 32;

    /// <summary>Texture page word the panel polygon uses: 4-bit page at VRAM x 0x140, rate 2.</summary>
    public const int PanelTexturePage = 0x45;

    /// <summary>CLUT word the panel polygon uses: VRAM x 368, y 177.</summary>
    public const int PanelClut = 0x2C57;

    // The menu texture is loaded at VRAM (0x140, 0), so VRAM x maps to texture x - 0x140.
    private const int TextureVramX = 0x140;
    // The panel samples texels u 0xBC..0xC7 (inclusive) and v 0..0x3D of texture page 0.
    private const int PanelU0 = 0xBC;
    private const int PanelU1 = 0xC7;
    private const int PanelV0 = 0;
    private const int PanelV1 = 0x3E;
    // A talk-box frame record holds at most sixteen parts.
    private const int MaxFrameParts = 16;

    /// <summary>Parses the four layout records from the executable's 128-byte layout table.</summary>
    /// <param name="table">The exact 128-byte little-endian table read from the field executable.</param>
    /// <exception cref="FormatException">The table length is wrong or a sprite id is negative.</exception>
    public static IReadOnlyList<TalkBoxLayoutRecord> ReadLayouts(ReadOnlySpan<byte> table)
    {
        if (table.Length != LayoutCount * LayoutRecordBytes)
            throw new FormatException($"Talk-box layout table is {table.Length} bytes, expected {LayoutCount * LayoutRecordBytes}.");
        var layouts = new TalkBoxLayoutRecord[LayoutCount];
        for (int layout = 0; layout < LayoutCount; layout++)
        {
            ReadOnlySpan<byte> record = table.Slice(layout * LayoutRecordBytes, LayoutRecordBytes);
            int sprite = BinaryPrimitives.ReadInt32LittleEndian(record);
            if (sprite < 0)
                throw new FormatException($"Talk-box layout {layout} has a negative sprite id {sprite}.");
            var field = new int[14];
            for (int index = 0; index < field.Length; index++)
                field[index] = BinaryPrimitives.ReadInt16LittleEndian(record[(4 + index * 2)..]);
            layouts[layout] = new TalkBoxLayoutRecord(sprite,
                field[0], field[1], field[2], field[3], field[4], field[5], field[6], field[7],
                field[8], field[9], field[10], field[11], field[12], field[13]);
        }
        return layouts;
    }

    /// <summary>
    /// Renders one talk-box frame. A frame whose semi-transparency rate is -1 is opaque; rates
    /// 0..3 are semi-transparent. Texels carrying the STP bit keep alpha 128.
    /// </summary>
    /// <param name="parts">Decoded menu-sprite definitions referenced by the requested frame.</param>
    /// <param name="ids">Frame identifiers in the same order as <paramref name="frameOffsets"/>.</param>
    /// <param name="frameOffsets">Byte offsets, after the four-byte sprite-sheet header, for each frame.</param>
    /// <param name="sheet">Complete menu sprite-sheet bytes, including its four-byte header.</param>
    /// <param name="pages">Menu texture pages used by the source sprite renderer.</param>
    /// <param name="frameId">Source frame identifier requested by the dialogue layout.</param>
    /// <param name="clutFrames">Positive count of palette animation frames carried by the talk box.</param>
    /// <returns>The rendered talk-box sprite and its source transparency metadata.</returns>
    /// <exception cref="FormatException">The frame is absent, has too many parts or an invalid rate.</exception>
    public static TalkBoxFrameImage RenderFrame(
        IReadOnlyList<HudSpritePart> parts,
        IReadOnlyList<int> ids,
        IReadOnlyList<int> frameOffsets,
        byte[] sheet,
        IReadOnlyList<HudTexturePage> pages,
        int frameId,
        int clutFrames)
    {
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentNullException.ThrowIfNull(frameOffsets);
        ArgumentNullException.ThrowIfNull(sheet);
        if (clutFrames <= 0) throw new ArgumentOutOfRangeException(nameof(clutFrames));
        int position = FindFramePosition(ids, frameId);
        if (position < 0)
        {
            throw new FormatException($"Menu sprite sheet lacks frame {frameId}.");
        }

        ReadOnlySpan<byte> body = sheet.AsSpan(4);
        (int offset, short rate) = ReadFrameMetadata(body, frameOffsets[position], frameId);

        var (rgba, width, height, _, _) = BattleHudDecoder.RenderFieldFrame(parts, body, offset, pages);
        return new TalkBoxFrameImage(frameId, width, height, clutFrames, rate != -1, rgba);
    }

    /// <summary>
    /// Decodes the panel's 12x62 texture from texture page 0 through the panel CLUT. The body
    /// colour is the most frequent opaque texel colour (first seen wins a tie).
    /// </summary>
    /// <param name="pages">Decoded menu texture pages whose first page embeds the panel and CLUT.</param>
    /// <returns>The panel pixels, dominant opaque colour, and original texture-page identifiers.</returns>
    /// <exception cref="FormatException">The texture or CLUT is absent, or the panel has no opaque texel.</exception>
    public static TalkBoxPanelImage DecodePanel(IReadOnlyList<HudTexturePage> pages)
    {
        ArgumentNullException.ThrowIfNull(pages);
        IReadOnlyList<ushort> clutRow = ReadPanelClut(pages);
        (byte[] rgba, List<(byte R, byte G, byte B)> order, Dictionary<(byte R, byte G, byte B), int> counts) = DecodePanelPixels(pages[0], clutRow);
        if (order.Count == 0)
            throw new FormatException("Talk-box panel texture has no opaque texel.");
        (byte R, byte G, byte B) body = order[0];
        foreach ((byte R, byte G, byte B) colour in order)
        {
            if (counts[colour] > counts[body])
            {
                body = colour;
            }
        }

        return new TalkBoxPanelImage(PanelU1 - PanelU0 + 1, PanelV1 - PanelV0, body.R, body.G, body.B, PanelTexturePage, PanelClut, rgba);
    }

    /// <summary>Finds a dialogue frame identifier in the source order used by its offset table.</summary>
    /// <param name="ids">Source frame identifiers paired with byte offsets.</param>
    /// <param name="frameId">Frame identifier requested by a layout record.</param>
    /// <returns>The matching offset-table index, or -1 when the frame is absent.</returns>
    private static int FindFramePosition(IReadOnlyList<int> ids, int frameId)
    {
        for (int index = 0; index < ids.Count; index++)
        {
            if (ids[index] == frameId)
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>Reads and validates the fixed six-byte header preceding a talk-box frame part list.</summary>
    /// <param name="body">Sprite-sheet bytes after their four-byte archive header.</param>
    /// <param name="offset">Frame header offset inside <paramref name="body"/>.</param>
    /// <param name="frameId">Identifier used in diagnostics for the malformed source record.</param>
    /// <returns>The retained offset and source semi-transparency rate.</returns>
    /// <exception cref="FormatException">The frame header, part count, or blend rate is invalid.</exception>
    private static (int Offset, short Rate) ReadFrameMetadata(ReadOnlySpan<byte> body, int offset, int frameId)
    {
        if (offset + 6 > body.Length)
        {
            throw new FormatException($"Menu sprite frame {frameId} record is truncated.");
        }

        short count = BinaryPrimitives.ReadInt16LittleEndian(body[offset..]);
        short rate = BinaryPrimitives.ReadInt16LittleEndian(body[(offset + 4)..]);
        if (count is <= 0 or > MaxFrameParts)
        {
            throw new FormatException($"Menu sprite frame {frameId} has invalid part count {count}.");
        }

        if (rate is < -1 or > 3)
        {
            throw new FormatException($"Menu sprite frame {frameId} has invalid semi-transparency rate {rate}.");
        }

        return (offset, rate);
    }

    /// <summary>Locates the 16-word panel CLUT in the menu texture pages.</summary>
    /// <param name="pages">Decoded pages that include the panel palette row.</param>
    /// <returns>The complete source palette row containing the panel CLUT.</returns>
    /// <exception cref="FormatException">The CLUT page or its 16 words are unavailable.</exception>
    private static IReadOnlyList<ushort> ReadPanelClut(IReadOnlyList<HudTexturePage> pages)
    {
        int clutX = ((PanelClut & 0x3F) * 16) - TextureVramX;
        int clutY = PanelClut >> 6;
        int clutPage = clutX / BattleHudDecoder.PageWidthWords;
        int clutWord = clutX % BattleHudDecoder.PageWidthWords;
        if (pages.Count == 0 || clutPage >= pages.Count || clutY >= pages[clutPage].Height)
        {
            throw new FormatException("Talk-box panel CLUT lies outside the loaded menu texture.");
        }

        IReadOnlyList<ushort> clutRow = pages[clutPage].Rows[clutY];
        if (clutWord + 16 > clutRow.Count)
        {
            throw new FormatException("Talk-box panel CLUT row is truncated.");
        }

        return clutRow;
    }

    /// <summary>Expands the fixed panel rectangle and counts opaque colours in first-seen order.</summary>
    /// <param name="texture">Texture page zero containing the panel's packed 4-bit texels.</param>
    /// <param name="clutRow">Palette row containing the panel's 16 colour entries.</param>
    /// <returns>RGBA pixels, opaque colour order, and opaque colour occurrence counts.</returns>
    private static (byte[] Rgba, List<(byte R, byte G, byte B)> Order, Dictionary<(byte R, byte G, byte B), int> Counts) DecodePanelPixels(HudTexturePage texture, IReadOnlyList<ushort> clutRow)
    {
        int width = PanelU1 - PanelU0 + 1;
        int clutWord = (((PanelClut & 0x3F) * 16) - TextureVramX) % BattleHudDecoder.PageWidthWords;
        var rgba = new byte[width * (PanelV1 - PanelV0) * 4];
        var counts = new Dictionary<(byte R, byte G, byte B), int>();
        var order = new List<(byte R, byte G, byte B)>();
        for (int v = PanelV0; v < PanelV1; v++)
        {
            DecodePanelRow(texture.Rows[v], clutRow, clutWord, v, rgba, counts, order);
        }

        return (rgba, order, counts);
    }

    /// <summary>Expands one packed panel row and records opaque colours for the body-colour vote.</summary>
    /// <param name="row">Packed source texels at the selected vertical coordinate.</param>
    /// <param name="clutRow">Palette row holding the panel CLUT.</param>
    /// <param name="clutWord">Word offset of the 16-colour CLUT inside <paramref name="clutRow"/>.</param>
    /// <param name="v">Panel texture v coordinate of <paramref name="row"/>.</param>
    /// <param name="rgba">Destination RGBA buffer.</param>
    /// <param name="counts">Opaque colour occurrence counters.</param>
    /// <param name="order">First-seen opaque colours.</param>
    private static void DecodePanelRow(IReadOnlyList<ushort> row, IReadOnlyList<ushort> clutRow, int clutWord, int v, byte[] rgba, Dictionary<(byte R, byte G, byte B), int> counts, List<(byte R, byte G, byte B)> order)
    {
        int width = PanelU1 - PanelU0 + 1;
        for (int u = PanelU0; u <= PanelU1; u++)
        {
            int index = (row[u / 4] >> ((u & 3) * 4)) & 0xF;
            ushort word = clutRow[clutWord + index];
            if (word == 0)
            {
                continue;
            }

            byte r = BattleHudDecoder.Ps1Channel(word & 31);
            byte g = BattleHudDecoder.Ps1Channel((word >> 5) & 31);
            byte b = BattleHudDecoder.Ps1Channel((word >> 10) & 31);
            bool stp = (word & 0x8000) != 0;
            int at = (((v - PanelV0) * width) + (u - PanelU0)) * 4;
            rgba[at] = r;
            rgba[at + 1] = g;
            rgba[at + 2] = b;
            rgba[at + 3] = stp ? (byte)128 : byte.MaxValue;
            if (!stp)
            {
                RecordOpaqueColour((r, g, b), counts, order);
            }
        }
    }

    /// <summary>Increments one opaque colour and preserves the source's first-seen tie ordering.</summary>
    /// <param name="colour">Decoded non-STP RGB colour to count.</param>
    /// <param name="counts">Occurrence count keyed by RGB triplet.</param>
    /// <param name="order">Colours in their first source appearance order.</param>
    private static void RecordOpaqueColour((byte R, byte G, byte B) colour, Dictionary<(byte R, byte G, byte B), int> counts, List<(byte R, byte G, byte B)> order)
    {
        if (counts.TryGetValue(colour, out int seen))
        {
            counts[colour] = seen + 1;
            return;
        }

        counts.Add(colour, 1);
        order.Add(colour);
    }
}
