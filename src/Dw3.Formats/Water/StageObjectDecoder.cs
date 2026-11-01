using Dw3.Formats.Field;

namespace Dw3.Formats.Water;

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
    /// <param name="stageId">The three-digit stage identifier used in decode errors.</param>
    /// <param name="overlay">The WSTAG overlay containing the descriptor and StageTile table.</param>
    /// <param name="tmpk">The TMPK archive containing the selected sprite sheet.</param>
    /// <param name="mask">The MASK archive containing object VRAM pages.</param>
    /// <returns>The rendered visible map objects and decoded VRAM page count.</returns>
    /// <exception cref="FormatException">A file is malformed, a frame is missing, or the stage has no drawable object.</exception>
    public static StageObjectSet Decode(int stageId, ReadOnlySpan<byte> overlay, ReadOnlyMemory<byte> tmpk, ReadOnlySpan<byte> mask)
    {
        StageDescriptor descriptor = StageDescriptorReader.Read(overlay);
        IReadOnlyList<StageTile> tiles = StageTileDecoder.Decode(overlay, descriptor.ObjectsAddress);
        SpriteSheet sheet = SpriteSheetDecoder.Decode(SpriteSheetDecoder.ReadArchiveEntry(tmpk, descriptor.SheetEntry));
        ObjectVram vram = ObjectTextureArchiveDecoder.Decode(mask);
        IReadOnlyList<StageObject> objects = DecodeObjects(stageId, tiles, sheet, vram);
        EnsureObjectsPresent(stageId, objects);
        return new StageObjectSet(objects, vram.Pages.Count);
    }

    /// <summary>Decodes the visible source records into drawable objects.</summary>
    /// <param name="stageId">The three-digit stage identifier used in decode errors.</param>
    /// <param name="tiles">The source StageTile records.</param>
    /// <param name="sheet">The decoded sprite sheet selected by the descriptor.</param>
    /// <param name="vram">The decoded object VRAM pages.</param>
    /// <returns>The drawable objects in source table order.</returns>
    private static List<StageObject> DecodeObjects(int stageId, IReadOnlyList<StageTile> tiles, SpriteSheet sheet, ObjectVram vram)
    {
        var objects = new List<StageObject>();
        for (int index = 0; index < tiles.Count; index++)
        {
            StageTile tile = tiles[index];
            if (IsNotDrawable(tile))
            {
                continue;
            }

            IReadOnlyList<StageObjectFrame> frames = StageObjectFrameDecoder.Decode(stageId, index, tile, sheet, vram);
            objects.Add(new StageObject(index, tile, frames));
        }

        return objects;
    }

    /// <summary>Determines whether the record selects no local drawable sprite.</summary>
    /// <param name="tile">The source record to classify.</param>
    /// <returns><see langword="true"/> when the record is hidden or uses the shared archive.</returns>
    private static bool IsNotDrawable(StageTile tile) => tile.Visible == 0 || tile.Anim == SharedSpriteArchive;

    /// <summary>Rejects a stage whose source table produced no local drawable objects.</summary>
    /// <param name="stageId">The three-digit stage identifier used in the error.</param>
    /// <param name="objects">The decoded drawable objects.</param>
    /// <exception cref="FormatException">No object was decoded.</exception>
    private static void EnsureObjectsPresent(int stageId, IReadOnlyCollection<StageObject> objects)
    {
        if (objects.Count == 0)
        {
            throw new FormatException($"S{stageId:D3} has no visible source map objects");
        }
    }
}
