using System.Text.Json;
using Dw3.Data;
using Dw3.Formats.Battle;
using Dw3.Formats.Binary;
using Dw3.Formats.Binary.Png;

namespace Dw3.Extractor;

/// <summary>Extracts the common contact effect using the supported edition's sheet and animation archive.</summary>
internal static class BattleImpactExtractor
{
    /// <summary>Writes decoded contact images and animation channels, never raw disc entries.</summary>
    /// <param name="context">Atomic pack output.</param>
    /// <param name="files">Disc member resolver.</param>
    internal static void Write(DataPackWriteContext context, EngineFileReader files)
    {
        byte[] archive = files.Read(0x79D).Bytes;
        byte[] sheet = Entry(archive, 0x26);
        var wrapped = new byte[sheet.Length + 4];
        sheet.CopyTo(wrapped, 4);
        var parsed = HudSpriteSheetParser.Parse(wrapped);
        byte[] texture = Entry(files.Read(0x79E).Bytes, 0);
        var pages = new[] { HudTexturePageDecoder.DecodeFourBitTim(RlenDecoder.Decompress(texture)) };
        byte[] effect = Entry(archive, 2);
        var animations = ModelArchive.ReadBounds(effect).Where(range => !range.IsEmpty)
            .Select(range => BattleSpriteAnimationDecoder.Decode(effect.AsSpan(range.Start, range.End - range.Start))).ToArray();
        string directory = context.GetImagePath("battle/impact");
        Directory.CreateDirectory(directory);
        var frames = animations.SelectMany(animation => animation).Select(sample => (Frame: sample[0], Row: sample[1])).Distinct()
            .Select(key => WriteFrame(context, key, sheet, parsed, pages)).ToArray();
        File.WriteAllText(Path.Combine(directory, "effect-2.json"), JsonSerializer.Serialize(new { schema = "battle-impact-v1", animations, frames }));
    }

    /// <summary>Renders one referenced frame and palette row with its original pivot and blend mode.</summary>
    /// <param name="context">Pack output.</param>
    /// <param name="key">Frame and palette identity.</param>
    /// <param name="sheet">Sprite sheet body.</param>
    /// <param name="parsed">Parsed sheet tables.</param>
    /// <param name="pages">Loaded common texture page.</param>
    /// <returns>JSON-compatible frame metadata.</returns>
    private static object WriteFrame(
        DataPackWriteContext context,
        (short Frame, short Row) key,
        byte[] sheet,
        (IReadOnlyList<HudSpritePart> Parts, IReadOnlyList<int> Ids, IReadOnlyList<int> FrameOffsets) parsed,
        IReadOnlyList<HudTexturePage> pages)
    {
        int index = parsed.Ids.ToList().IndexOf(key.Frame);
        if (index < 0)
        {
            throw new FormatException($"Impact frame {key.Frame} is absent from the common sheet.");
        }

        var picture = HudFrameRenderer.RenderFrame(parsed.Parts, sheet, parsed.FrameOffsets[index], pages, key.Row);
        string path = $"battle/impact/frame-{key.Frame}-row-{key.Row}.png";
        File.WriteAllBytes(context.GetImagePath(path), PngEncoder.Encode(picture.Width, picture.Height, picture.Rgba));
        context.RegisterImage(path);
        return new { frame = key.Frame, row = key.Row, path, x = picture.OffsetX, y = picture.OffsetY, abr = picture.Abr };
    }

    /// <summary>Returns a validated archive member.</summary>
    /// <param name="archive">Complete archive.</param>
    /// <param name="index">Entry index.</param>
    /// <returns>Bounded bytes of the entry.</returns>
    private static byte[] Entry(byte[] archive, int index)
    {
        ArchiveRange range = ModelArchive.ReadBounds(archive)[index];
        return archive[range.Start..range.End];
    }
}
