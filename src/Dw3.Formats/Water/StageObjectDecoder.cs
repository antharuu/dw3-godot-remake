using Dw3.Formats.Field;

namespace Dw3.Formats.Water;

/// <summary>One rendered frame of a map object at one palette row.</summary>
/// <param name="FrameId">Sprite-sheet frame id.</param>
/// <param name="ClutRow">Palette row shift used for this rendering.</param>
/// <param name="Image">Composed colour words.</param>
public sealed record StageObjectFrame(int FrameId, int ClutRow, SpriteFrameImage Image);

/// <summary>A drawable map object and every frame its animation cycle can show.</summary>
/// <param name="Index">Position of the record in the StageTile table.</param>
/// <param name="Tile">The decoded StageTile record.</param>
/// <param name="Frames">Frames in cycle order: palette rows outermost, frame ids innermost.</param>
public sealed record StageObject(int Index, StageTile Tile, IReadOnlyList<StageObjectFrame> Frames);

/// <summary>The drawable map objects of one stage and the number of object VRAM pages loaded.</summary>
public sealed record StageObjectSet(IReadOnlyList<StageObject> Objects, int ArchiveEntries);

/// <summary>
/// Decodes and renders the map objects of one stage from its three disc files: the WSTAG
/// overlay (StageTile table and descriptor), the TMPK archive (sprite sheet) and the MASK
/// archive (object VRAM).
///
/// Only visible records backed by the stage sheet are drawn: anim 0xFF selects the shared
/// hidden-spot sprites, which live outside these files. A frame-cycling object (cycle 1)
/// renders frames CycleFirst..CycleLast at its own palette row; a palette-cycling object
/// (cycle 2 or 3) renders its frame at palette rows CycleFirst..CycleLast; a static object
/// renders its frame at its palette row.
/// </summary>
public static class StageObjectDecoder
{
    private const byte SharedSpriteArchive = 0xFF;

    /// <summary>Decodes every drawable object of stage <paramref name="stageId"/>.</summary>
    /// <exception cref="FormatException">A file is malformed, a frame is missing, or the stage has no drawable object.</exception>
    public static StageObjectSet Decode(int stageId, ReadOnlySpan<byte> overlay, ReadOnlyMemory<byte> tmpk, ReadOnlySpan<byte> mask)
    {
        StageDescriptor descriptor = StageDescriptorReader.Read(overlay);
        IReadOnlyList<StageTile> tiles = StageTileDecoder.Decode(overlay, descriptor.ObjectsAddress);
        SpriteSheet sheet = SpriteSheetDecoder.Decode(SpriteSheetDecoder.ReadArchiveEntry(tmpk, descriptor.SheetEntry));
        ObjectVram vram = ObjectTextureArchiveDecoder.Decode(mask);

        var objects = new List<StageObject>();
        for (int index = 0; index < tiles.Count; index++)
        {
            StageTile tile = tiles[index];
            if (tile.Visible == 0 || tile.Anim == SharedSpriteArchive)
                continue;
            IEnumerable<int> frameIds = tile.Cycle == 1 ? Range(tile.CycleFirst, tile.CycleLast) : [tile.Frame];
            IEnumerable<int> clutRows = tile.Cycle is 2 or 3 ? Range(tile.CycleFirst, tile.CycleLast) : [tile.ClutRow];
            var frames = new List<StageObjectFrame>();
            foreach (int clutRow in clutRows)
                foreach (int frameId in frameIds)
                {
                    if (!sheet.Frames.TryGetValue(frameId, out ReadOnlyMemory<byte> frame))
                        throw new FormatException($"S{stageId:D3} object={index} frame=0x{frameId:X} absent");
                    frames.Add(new StageObjectFrame(frameId, clutRow, SpriteFrameRenderer.Render(sheet.Parts, frame.Span, vram, clutRow)));
                }
            objects.Add(new StageObject(index, tile, frames));
        }
        if (objects.Count == 0)
            throw new FormatException($"S{stageId:D3} has no visible source map objects");
        return new StageObjectSet(objects, vram.Pages.Count);
    }

    private static IEnumerable<int> Range(int first, int last) => Enumerable.Range(first, last - first + 1);
}
