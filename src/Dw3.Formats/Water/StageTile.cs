namespace Dw3.Formats.Water;

/// <summary>
/// One 18-byte StageTile record: a map object (water, furniture, decoration) drawn from
/// the stage sprite sheet.
/// </summary>
/// <param name="Visible">1 when the object is drawn, 0 when hidden.</param>
/// <param name="Anim">Archive selector; 0xFF selects the shared hidden-spot sprites instead of the stage sheet.</param>
/// <param name="Margin">Culling margin; 0 terminates the table.</param>
/// <param name="Depth">Draw layer.</param>
/// <param name="Frame">Sprite-sheet frame id when the object does not cycle frames.</param>
/// <param name="Cycle">0 static, 1 frame cycle, 2 and 3 palette-row cycles.</param>
/// <param name="CycleFirst">First frame id or palette row of the cycle.</param>
/// <param name="CycleLast">Last frame id or palette row of the cycle (inclusive).</param>
/// <param name="CycleDelay">Frames between two cycle steps.</param>
/// <param name="ClutRow">Palette row offset when the object does not cycle palettes.</param>
/// <param name="X">Map X position.</param>
/// <param name="Y">Map Y position.</param>
/// <param name="SortY">Y coordinate used for depth sorting.</param>
/// <param name="CycleTime">Initial cycle timer.</param>
/// <param name="SourceAddress">RAM address of this record inside the overlay.</param>
public sealed record StageTile(
    byte Visible, byte Anim, byte Margin, byte Depth, byte Frame, byte Cycle, byte CycleFirst, byte CycleLast,
    byte CycleDelay, byte ClutRow, short X, short Y, short SortY, short CycleTime, uint SourceAddress);
