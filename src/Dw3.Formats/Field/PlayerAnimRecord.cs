namespace Dw3.Formats.Field;

/// <summary>One record in an animation sequence (duration + frame + draw offset).</summary>
public sealed class PlayerAnimRecord
{
    /// <summary>Duration in game ticks (> 0).</summary>
    public int Duration { get; }

    /// <summary>Zero-based index into the frame table (&lt; FrameCount).</summary>
    public int Frame { get; }

    /// <summary>Draw offset X in pixels (tile-relative, from FIELDSTG_drawActor).</summary>
    public int OffsetX { get; }

    /// <summary>Draw offset Y in pixels (tile-relative, from FIELDSTG_drawActor).</summary>
    public int OffsetY { get; }

    internal PlayerAnimRecord(int duration, int frame, int offsetX, int offsetY)
    {
        Duration = duration;
        Frame = frame;
        OffsetX = offsetX;
        OffsetY = offsetY;
    }
}
