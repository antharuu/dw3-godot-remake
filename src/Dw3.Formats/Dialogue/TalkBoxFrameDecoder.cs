using System.Buffers.Binary;
using Dw3.Formats.Battle;

namespace Dw3.Formats.Dialogue;

/// <summary>One talk-box layout record: where each part of the box is drawn for one box type.</summary>
public sealed record TalkBoxLayoutRecord(
    int Sprite,
    int SpriteX, int SpriteY,
    int Sprite0X, int Sprite0Y,
    int PanelX, int PanelY,
    int Sprite2X, int Sprite2Y,
    int NameX, int NameY,
    int TextX, int TextY,
    int ArrowX, int ArrowY);

/// <summary>One composited talk-box frame sprite in RGBA order.</summary>
public sealed record TalkBoxFrameImage(int FrameId, int Width, int Height, int ClutFrames, bool Semi, byte[] Rgba);

/// <summary>The talk-box panel texture in RGBA order, plus its dominant opaque body colour.</summary>
public sealed record TalkBoxPanelImage(int Width, int Height, int Red, int Green, int Blue, int TexturePage, int Clut, byte[] Rgba);

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
        int position = -1;
        for (int index = 0; index < ids.Count; index++)
            if (ids[index] == frameId) { position = index; break; }
        if (position < 0)
            throw new FormatException($"Menu sprite sheet lacks frame {frameId}.");

        ReadOnlySpan<byte> body = sheet.AsSpan(4);
        int offset = frameOffsets[position];
        if (offset + 6 > body.Length)
            throw new FormatException($"Menu sprite frame {frameId} record is truncated.");
        short count = BinaryPrimitives.ReadInt16LittleEndian(body[offset..]);
        short rate = BinaryPrimitives.ReadInt16LittleEndian(body[(offset + 4)..]);
        if (count is <= 0 or > MaxFrameParts)
            throw new FormatException($"Menu sprite frame {frameId} has invalid part count {count}.");
        if (rate is < -1 or > 3)
            throw new FormatException($"Menu sprite frame {frameId} has invalid semi-transparency rate {rate}.");

        var (rgba, width, height, _, _) = BattleHudDecoder.RenderFieldFrame(parts, body, offset, pages);
        return new TalkBoxFrameImage(frameId, width, height, clutFrames, rate != -1, rgba);
    }

    /// <summary>
    /// Decodes the panel's 12x62 texture from texture page 0 through the panel CLUT. The body
    /// colour is the most frequent opaque texel colour (first seen wins a tie).
    /// </summary>
    /// <exception cref="FormatException">The texture or CLUT is absent, or the panel has no opaque texel.</exception>
    public static TalkBoxPanelImage DecodePanel(IReadOnlyList<HudTexturePage> pages)
    {
        ArgumentNullException.ThrowIfNull(pages);
        int clutX = (PanelClut & 0x3F) * 16 - TextureVramX;
        int clutY = PanelClut >> 6;
        int clutPage = clutX / BattleHudDecoder.PageWidthWords;
        int clutWord = clutX % BattleHudDecoder.PageWidthWords;
        if (pages.Count == 0 || clutPage >= pages.Count || clutY >= pages[clutPage].Height)
            throw new FormatException("Talk-box panel CLUT lies outside the loaded menu texture.");
        IReadOnlyList<ushort> clutRow = pages[clutPage].Rows[clutY];
        if (clutWord + 16 > clutRow.Count)
            throw new FormatException("Talk-box panel CLUT row is truncated.");

        int width = PanelU1 - PanelU0 + 1;
        int height = PanelV1 - PanelV0;
        var rgba = new byte[width * height * 4];
        var counts = new Dictionary<(byte R, byte G, byte B), int>();
        var order = new List<(byte R, byte G, byte B)>();
        HudTexturePage texture = pages[0];
        for (int v = PanelV0; v < PanelV1; v++)
        {
            IReadOnlyList<ushort> row = texture.Rows[v];
            for (int u = PanelU0; u <= PanelU1; u++)
            {
                int index = (row[u / 4] >> ((u & 3) * 4)) & 0xF;
                ushort word = clutRow[clutWord + index];
                if (word == 0) continue;
                byte r = BattleHudDecoder.Ps1Channel(word & 31);
                byte g = BattleHudDecoder.Ps1Channel((word >> 5) & 31);
                byte b = BattleHudDecoder.Ps1Channel((word >> 10) & 31);
                bool stp = (word & 0x8000) != 0;
                int at = ((v - PanelV0) * width + (u - PanelU0)) * 4;
                rgba[at] = r; rgba[at + 1] = g; rgba[at + 2] = b; rgba[at + 3] = stp ? (byte)128 : (byte)255;
                if (stp) continue;
                if (counts.TryGetValue((r, g, b), out int seen)) counts[(r, g, b)] = seen + 1;
                else { counts.Add((r, g, b), 1); order.Add((r, g, b)); }
            }
        }
        if (order.Count == 0)
            throw new FormatException("Talk-box panel texture has no opaque texel.");
        (byte R, byte G, byte B) body = order[0];
        foreach ((byte R, byte G, byte B) colour in order)
            if (counts[colour] > counts[body]) body = colour;
        return new TalkBoxPanelImage(width, height, body.R, body.G, body.B, PanelTexturePage, PanelClut, rgba);
    }
}
