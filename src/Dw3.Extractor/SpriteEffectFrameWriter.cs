using System.Buffers.Binary;
using Dw3.Data;
using Dw3.Disc;
using Dw3.Formats.Battle;
using Dw3.Formats.Binary.Png;

namespace Dw3.Extractor;

/// <summary>Writes palette-specific sprite frames and their source part/palette metadata.</summary>
internal sealed class SpriteEffectFrameWriter
{
    private readonly byte[] _sheet;
    private readonly IReadOnlyList<HudTexturePage> _pages;
    private readonly IReadOnlyList<HudSpritePart> _parts;
    private readonly IReadOnlyList<int> _ids;
    private readonly IReadOnlyList<int> _offsets;

    /// <summary>Initializes a new instance of the <see cref="SpriteEffectFrameWriter"/> class.</summary>
    /// <param name="sheet">Unwrapped sheet body.</param>
    /// <param name="pages">Decoded texture pages.</param>
    internal SpriteEffectFrameWriter(byte[] sheet, IReadOnlyList<HudTexturePage> pages)
    {
        _sheet = sheet;
        _pages = pages;
        var wrapped = new byte[sheet.Length + 4];
        sheet.CopyTo(wrapped, 4);
        (_parts, _ids, _offsets) = HudSpriteSheetParser.Parse(wrapped);
    }

    /// <summary>Gets source texture/palette part definitions, including unresolved frame dependencies.</summary>
    internal IReadOnlyList<HudSpritePart> Parts => _parts;

    /// <summary>Gets all source frame definitions independently of palette availability.</summary>
    internal object[] Definitions => [.. _ids.Select((id, i) => Definition(id, _offsets[i]))];

    /// <summary>Writes one actual frame/CLUT combination referenced by an animation sample.</summary>
    /// <param name="context">Atomic pack writer.</param>
    /// <param name="id">Sprite effect identity.</param>
    /// <param name="key">Frame id and palette row offset.</param>
    /// <returns>Portable frame metadata including original CLUT words.</returns>
    internal object Write(DataPackWriteContext context, int id, (short Frame, short Row) key)
    {
        int index = _ids.ToList().IndexOf(key.Frame);
        if (index < 0)
        {
            throw new FormatException($"Sprite {id} references missing frame {key.Frame}.");
        }

        int offset = _offsets[index];
        HudFrameHeader header = ReadHeader(offset);
        HudRenderedPart[] rendered = [.. Enumerable.Range(0, header.Count).Select(i => RenderPart(offset, i, key.Row))];
        var (image, x, y) = HudAlphaCompositor.ComposeBackToFront(rendered);
        string path = $"battle/sprites/{id}/frame-{key.Frame}-row-{key.Row}.png";
        Directory.CreateDirectory(Path.GetDirectoryName(context.GetImagePath(path))!);
        byte[] png = PngEncoder.Encode(image.Width, image.Height, image.Pixels);
        File.WriteAllBytes(context.GetImagePath(path), png);
        context.RegisterImage(path);
        var parts = Enumerable.Range(0, header.Count).Select(i => Part(context, id, key, offset, i, header)).ToArray();
        return new { frame = key.Frame, row = key.Row, path, sha256 = DiscIdentity.Sha256Bytes(png), width = image.Width, height = image.Height, x, y, abr = header.Abr, headerRow = header.Row, parts };
    }

    /// <summary>Retains source geometry even when a palette lies outside loaded pages.</summary>
    /// <param name="id">Frame identity.</param>
    /// <param name="offset">Frame body offset.</param>
    /// <returns>Source header and placements.</returns>
    private object Definition(int id, int offset)
    {
        HudFrameHeader header = ReadHeader(offset);
        var placements = Enumerable.Range(0, header.Count).Select(i => new[]
        {
            BinaryPrimitives.ReadInt16LittleEndian(_sheet.AsSpan(offset + 6 + (i * 6))),
            BinaryPrimitives.ReadInt16LittleEndian(_sheet.AsSpan(offset + 8 + (i * 6))),
            BinaryPrimitives.ReadInt16LittleEndian(_sheet.AsSpan(offset + 10 + (i * 6))),
        }).ToArray();
        return new { id, row = header.Row, abr = header.Abr, placements };
    }

    /// <summary>Preserves each placed part's texture rectangle, palette words and pivot.</summary>
    /// <param name="context">Atomic pack writer.</param>
    /// <param name="id">Effect identity.</param>
    /// <param name="key">Frame and palette identity.</param>
    /// <param name="offset">Frame body offset.</param>
    /// <param name="index">Part placement index.</param>
    /// <param name="header">Source frame header.</param>
    /// <returns>JSON-compatible part contract.</returns>
    private object Part(DataPackWriteContext context, int id, (short Frame, short Row) key, int offset, int index, HudFrameHeader header)
    {
        int at = offset + 6 + (index * 6);
        int partIndex = BinaryPrimitives.ReadInt16LittleEndian(_sheet.AsSpan(at));
        HudSpritePart part = _parts[partIndex];
        ushort[] palette = HudFrameRenderer.ReadPalette(_pages, part.ClutX, part.ClutY + header.Row + key.Row, header.Abr >= 0);
        var layer = Layer(context, id, key, index, offset);
        return new
        {
            layer,
            index = partIndex,
            dx = BinaryPrimitives.ReadInt16LittleEndian(_sheet.AsSpan(at + 2)),
            dy = BinaryPrimitives.ReadInt16LittleEndian(_sheet.AsSpan(at + 4)),
            rectangle = new[] { part.U, part.V, part.Width, part.Height },
            clut = new[] { part.ClutX, part.ClutY, header.Row, key.Row },
            mode = part.Mode,
            palette,
        };
    }

    /// <summary>Writes a separate part image so source blend/order is not lost in a composite.</summary>
    /// <param name="context">Atomic pack writer.</param>
    /// <param name="id">Effect identity.</param>
    /// <param name="key">Frame and animated palette identity.</param>
    /// <param name="index">Placement ordinal in source draw order.</param>
    /// <param name="offset">Original frame header offset.</param>
    /// <returns>Layer path and content hash.</returns>
    private object Layer(DataPackWriteContext context, int id, (short Frame, short Row) key, int index, int offset)
    {
        HudRgbaImage image = RenderPart(offset, index, key.Row).Image;
        string path = $"battle/sprites/{id}/frame-{key.Frame}-row-{key.Row}-part-{index}.png";
        byte[] png = PngEncoder.Encode(image.Width, image.Height, image.Pixels);
        File.WriteAllBytes(context.GetImagePath(path), png);
        context.RegisterImage(path);
        return new { path, sha256 = DiscIdentity.Sha256Bytes(png), width = image.Width, height = image.Height };
    }

    /// <summary>Validates source frame placement bounds without the HUD-specific 64-part limit.</summary>
    /// <param name="offset">Frame body offset.</param>
    /// <returns>Source header with its unmodified blend and row values.</returns>
    private HudFrameHeader ReadHeader(int offset)
    {
        if (offset < 0 || offset > _sheet.Length - 6)
        {
            throw new FormatException("Action sprite frame header is truncated.");
        }

        short count = BinaryPrimitives.ReadInt16LittleEndian(_sheet.AsSpan(offset));
        if (count <= 0 || count > 256 || offset + 6 + (count * 6) > _sheet.Length)
        {
            throw new FormatException("Action sprite placement table is truncated or oversized.");
        }

        return new HudFrameHeader(count, BinaryPrimitives.ReadInt16LittleEndian(_sheet.AsSpan(offset + 2)), BinaryPrimitives.ReadInt16LittleEndian(_sheet.AsSpan(offset + 4)));
    }

    /// <summary>Decodes one layer while retaining original placement and ABR-minus-one semantics.</summary>
    /// <param name="offset">Source frame offset.</param>
    /// <param name="index">Part ordinal.</param>
    /// <param name="row">Animated additive palette row.</param>
    /// <returns>Decoded part at its original pivot.</returns>
    private HudRenderedPart RenderPart(int offset, int index, int row)
    {
        HudFrameHeader header = ReadHeader(offset);
        var body = new byte[12];
        _sheet.AsSpan(offset, 6).CopyTo(body);
        BinaryPrimitives.WriteInt16LittleEndian(body, 1);
        BinaryPrimitives.WriteInt16LittleEndian(body.AsSpan(4), header.Abr >= 0 ? (short)1 : (short)0);
        int at = offset + 6 + (index * 6);
        _sheet.AsSpan(at, 2).CopyTo(body.AsSpan(6));
        var picture = HudFrameRenderer.RenderFrame(_parts, body, 0, _pages, row);
        return new HudRenderedPart(BinaryPrimitives.ReadInt16LittleEndian(_sheet.AsSpan(at + 2)), BinaryPrimitives.ReadInt16LittleEndian(_sheet.AsSpan(at + 4)), new HudRgbaImage(picture.Rgba, picture.Width, picture.Height));
    }
}
