using System.Buffers.Binary;
using Dw3.Formats.Battle;
using Dw3.Formats.Dialogue;
using Xunit;

namespace Dw3.Formats.Tests.Dialogue;

/// <summary>
/// Talk-box frame decoder tests. Every buffer is fabricated in the test, never read from a disc.
/// </summary>
public sealed class TalkBoxFrameDecoderTests
{
    [Fact]
    public void ReadLayouts_ParsesFourThirtyTwoByteRecords()
    {
        byte[] table = new byte[TalkBoxFrameDecoder.LayoutCount * TalkBoxFrameDecoder.LayoutRecordBytes];
        for (int layout = 0; layout < TalkBoxFrameDecoder.LayoutCount; layout++)
        {
            Span<byte> record = table.AsSpan(layout * TalkBoxFrameDecoder.LayoutRecordBytes, TalkBoxFrameDecoder.LayoutRecordBytes);
            BinaryPrimitives.WriteInt32LittleEndian(record, 10 + layout);
            for (int field = 0; field < 14; field++)
                BinaryPrimitives.WriteInt16LittleEndian(record[(4 + field * 2)..], (short)(layout * 100 - field));
        }

        IReadOnlyList<TalkBoxLayoutRecord> layouts = TalkBoxFrameDecoder.ReadLayouts(table);

        Assert.Equal(4, layouts.Count);
        TalkBoxLayoutRecord third = layouts[2];
        Assert.Equal(12, third.Sprite);
        Assert.Equal((200, 199), (third.SpriteX, third.SpriteY));
        Assert.Equal((198, 197), (third.Sprite0X, third.Sprite0Y));
        Assert.Equal((196, 195), (third.PanelX, third.PanelY));
        Assert.Equal((194, 193), (third.Sprite2X, third.Sprite2Y));
        Assert.Equal((192, 191), (third.NameX, third.NameY));
        Assert.Equal((190, 189), (third.TextX, third.TextY));
        Assert.Equal((188, 187), (third.ArrowX, third.ArrowY));
    }

    [Fact]
    public void ReadLayouts_RejectsWrongTableLength()
    {
        Assert.Throws<FormatException>(() => TalkBoxFrameDecoder.ReadLayouts(new byte[TalkBoxFrameDecoder.LayoutRecordBytes * 3]));
    }

    [Fact]
    public void ReadLayouts_RejectsNegativeSpriteIndex()
    {
        byte[] table = new byte[TalkBoxFrameDecoder.LayoutCount * TalkBoxFrameDecoder.LayoutRecordBytes];
        BinaryPrimitives.WriteInt32LittleEndian(table, -1);
        Assert.Throws<FormatException>(() => TalkBoxFrameDecoder.ReadLayouts(table));
    }

    [Fact]
    public void DecodePanel_ReadsTextureColoursAndKeepsStpRows()
    {
        TalkBoxPanelImage panel = TalkBoxFrameDecoder.DecodePanel([PanelPage()]);

        Assert.Equal(12, panel.Width);
        Assert.Equal(62, panel.Height);
        Assert.Equal((222, 238, 255), (panel.Red, panel.Green, panel.Blue));
        Assert.Equal(TalkBoxFrameDecoder.PanelTexturePage, panel.TexturePage);
        Assert.Equal(TalkBoxFrameDecoder.PanelClut, panel.Clut);
        Assert.Equal(new byte[] { 222, 238, 255, 255 }, panel.Rgba[..4]);
        int last = (panel.Height - 1) * panel.Width * 4;
        Assert.Equal(new byte[] { 123, 123, 123, 128 }, panel.Rgba[last..(last + 4)]);
    }

    [Fact]
    public void DecodePanel_RejectsPageWithoutOpaqueTexel()
    {
        Assert.Throws<FormatException>(() => TalkBoxFrameDecoder.DecodePanel([EmptyPage()]));
    }

    [Fact]
    public void DecodePanel_RejectsMissingTexture()
    {
        Assert.Throws<FormatException>(() => TalkBoxFrameDecoder.DecodePanel([]));
    }

    [Fact]
    public void RenderFrame_ComposesFrameAndReportsSemiTransparency()
    {
        HudTexturePage page = SpritePage();
        byte[] sheet = BuildSheet([5], frameCount: 1, frameRow: 0, abr: 2);
        var (parts, ids, offsets) = BattleHudDecoder.ParseSheet(sheet);

        TalkBoxFrameImage frame = TalkBoxFrameDecoder.RenderFrame(parts, ids, offsets, sheet, [page], 5, clutFrames: 1);

        Assert.Equal(5, frame.FrameId);
        Assert.Equal((4, 2), (frame.Width, frame.Height));
        Assert.True(frame.Semi);
        Assert.Equal(1, frame.ClutFrames);
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, frame.Rgba[..4]);
    }

    [Fact]
    public void RenderFrame_ReportsOpaqueFrameAsNotSemi()
    {
        byte[] sheet = BuildSheet([7], frameCount: 1, frameRow: 0, abr: -1);
        var (parts, ids, offsets) = BattleHudDecoder.ParseSheet(sheet);

        TalkBoxFrameImage frame = TalkBoxFrameDecoder.RenderFrame(parts, ids, offsets, sheet, [SpritePage()], 7, clutFrames: 4);

        Assert.False(frame.Semi);
        Assert.Equal(4, frame.ClutFrames);
    }

    [Fact]
    public void RenderFrame_RejectsUnknownFrame()
    {
        byte[] sheet = BuildSheet([5], frameCount: 1, frameRow: 0, abr: 2);
        var (parts, ids, offsets) = BattleHudDecoder.ParseSheet(sheet);
        Assert.Throws<FormatException>(() => TalkBoxFrameDecoder.RenderFrame(parts, ids, offsets, sheet, [SpritePage()], 6, clutFrames: 1));
    }

    [Fact]
    public void RenderFrame_RejectsInvalidSemiTransparencyRate()
    {
        byte[] sheet = BuildSheet([5], frameCount: 1, frameRow: 0, abr: 4);
        var (parts, ids, offsets) = BattleHudDecoder.ParseSheet(sheet);
        Assert.Throws<FormatException>(() => TalkBoxFrameDecoder.RenderFrame(parts, ids, offsets, sheet, [SpritePage()], 5, clutFrames: 1));
    }

    [Fact]
    public void RenderFrame_RejectsTooManyParts()
    {
        byte[] sheet = BuildSheet([5], frameCount: 17, frameRow: 0, abr: 2);
        var (parts, ids, offsets) = BattleHudDecoder.ParseSheet(sheet);
        Assert.Throws<FormatException>(() => TalkBoxFrameDecoder.RenderFrame(parts, ids, offsets, sheet, [SpritePage()], 5, clutFrames: 1));
    }

    // The panel CLUT sits at page 0, halfword column 48, row 177. Index 11 is the light body
    // colour, index 14 the grey STP border used by the last two texel rows.
    private static HudTexturePage PanelPage()
    {
        ushort[][] rows = EmptyRows();
        rows[177][48 + 11] = 27 | (29 << 5) | (31 << 10);
        rows[177][48 + 14] = 0x8000 | 15 | (15 << 5) | (15 << 10);
        for (int v = 0; v < 62; v++)
        {
            int index = v >= 60 ? 14 : 11;
            ushort packed = (ushort)(index | (index << 4) | (index << 8) | (index << 12));
            for (int word = 0xBC / 4; word <= 0xC7 / 4; word++) rows[v][word] = packed;
        }
        return new HudTexturePage(rows);
    }

    // One 4x2 sprite part at texel (0,0) whose palette (row 10, column 0) maps index 1 to red.
    private static HudTexturePage SpritePage()
    {
        ushort[][] rows = EmptyRows();
        rows[10][1] = 31;
        rows[0][0] = 0x1111;
        rows[1][0] = 0x1111;
        return new HudTexturePage(rows);
    }

    private static HudTexturePage EmptyPage() => new(EmptyRows());

    private static ushort[][] EmptyRows()
    {
        var rows = new ushort[BattleHudDecoder.PageHeight][];
        for (int row = 0; row < rows.Length; row++) rows[row] = new ushort[BattleHudDecoder.PageWidthWords];
        return rows;
    }

    // Sheet layout: 4-byte leading pointer, then [partsOffset, idsOffset, frameOffset...],
    // one 14-byte part, the 0xFF-terminated id list, and one frame record per id.
    private static byte[] BuildSheet(int[] frameIds, int frameCount, short frameRow, short abr)
    {
        int header = 8 + 4 * frameIds.Length;
        int partsOffset = header;
        int idsOffset = partsOffset + 14;
        int frameOffset = idsOffset + frameIds.Length + 1;
        frameOffset += frameOffset % 2;
        int frameBytes = 6 + 6 * frameCount;
        byte[] body = new byte[frameOffset + frameBytes * frameIds.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(body, (uint)partsOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(4), (uint)idsOffset);
        short[] part = [0, 0, 4, 2, 0, 10, 0];
        for (int field = 0; field < part.Length; field++)
            BinaryPrimitives.WriteInt16LittleEndian(body.AsSpan(partsOffset + field * 2), part[field]);
        for (int index = 0; index < frameIds.Length; index++)
        {
            body[idsOffset + index] = (byte)frameIds[index];
            int at = frameOffset + index * frameBytes;
            BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(8 + index * 4), (uint)at);
            BinaryPrimitives.WriteInt16LittleEndian(body.AsSpan(at), (short)frameCount);
            BinaryPrimitives.WriteInt16LittleEndian(body.AsSpan(at + 2), frameRow);
            BinaryPrimitives.WriteInt16LittleEndian(body.AsSpan(at + 4), abr);
        }
        body[idsOffset + frameIds.Length] = 0xFF;
        byte[] sheet = new byte[4 + body.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(sheet, 4);
        body.CopyTo(sheet, 4);
        return sheet;
    }
}
