namespace Dw3.Formats.Water;

/// <summary>Renders every palette-and-frame combination selected by one StageTile record.</summary>
internal static class StageObjectFrameDecoder
{
    /// <summary>Renders the frames selected by a visible stage object record.</summary>
    /// <param name="stageId">The three-digit stage identifier used in decode errors.</param>
    /// <param name="objectIndex">The zero-based StageTile table index.</param>
    /// <param name="tile">The decoded tile that selects frames and palette rows.</param>
    /// <param name="sheet">The sprite-sheet frames and parts.</param>
    /// <param name="vram">The decoded object VRAM pages.</param>
    /// <returns>The rendered frames in palette-row then frame-id order.</returns>
    /// <exception cref="FormatException">A selected sprite-sheet frame is absent.</exception>
    internal static IReadOnlyList<StageObjectFrame> Decode(int stageId, int objectIndex, StageTile tile, SpriteSheet sheet, ObjectVram vram)
    {
        var frames = new List<StageObjectFrame>();
        foreach (int clutRow in SelectPaletteRows(tile))
        {
            foreach (int frameId in SelectFrameIds(tile))
            {
                frames.Add(RenderFrame(stageId, objectIndex, sheet, vram, clutRow, frameId));
            }
        }

        return frames;
    }

    /// <summary>Selects the frame ids in the record's source-defined render order.</summary>
    /// <param name="tile">The decoded tile that selects the frames.</param>
    /// <returns>One static frame or the complete frame cycle.</returns>
    private static IEnumerable<int> SelectFrameIds(StageTile tile) =>
        tile.Cycle == 1 ? Range(tile.CycleFirst, tile.CycleLast) : [tile.Frame];

    /// <summary>Selects the palette rows in the record's source-defined render order.</summary>
    /// <param name="tile">The decoded tile that selects the palette rows.</param>
    /// <returns>One static palette row or the complete palette cycle.</returns>
    private static IEnumerable<int> SelectPaletteRows(StageTile tile) =>
        tile.Cycle is 2 or 3 ? Range(tile.CycleFirst, tile.CycleLast) : [tile.ClutRow];

    /// <summary>Renders one requested frame at one requested palette row.</summary>
    /// <param name="stageId">The three-digit stage identifier used in errors.</param>
    /// <param name="objectIndex">The source StageTile table index.</param>
    /// <param name="sheet">The sprite-sheet frames and parts.</param>
    /// <param name="vram">The decoded object VRAM pages.</param>
    /// <param name="clutRow">The selected palette row.</param>
    /// <param name="frameId">The selected sprite-sheet frame id.</param>
    /// <returns>The rendered map-object frame.</returns>
    /// <exception cref="FormatException">The selected frame id is absent from the sheet.</exception>
    private static StageObjectFrame RenderFrame(int stageId, int objectIndex, SpriteSheet sheet, ObjectVram vram, int clutRow, int frameId)
    {
        if (!sheet.Frames.TryGetValue(frameId, out ReadOnlyMemory<byte> frame))
        {
            throw new FormatException($"S{stageId:D3} object={objectIndex} frame=0x{frameId:X} absent");
        }

        SpriteFrameImage image = SpriteFrameRenderer.Render(sheet.Parts, frame.Span, vram, clutRow);
        return new StageObjectFrame(frameId, clutRow, image);
    }

    /// <summary>Builds the inclusive source-defined cycle range.</summary>
    /// <param name="first">The first frame or palette-row value.</param>
    /// <param name="last">The final frame or palette-row value.</param>
    /// <returns>Every integer from <paramref name="first"/> through <paramref name="last"/>.</returns>
    private static IEnumerable<int> Range(int first, int last) => Enumerable.Range(first, last - first + 1);
}
