namespace Dw3.Formats.Field;

/// <summary>One record in an animation sequence (duration + frame + draw offset).</summary>
public sealed class PlayerAnimRecord
{
    /// <summary>Gets duration in game ticks (> 0).</summary>
    public int Duration { get; }

    /// <summary>Gets zero-based index into the frame table (&lt; FrameCount).</summary>
    public int Frame { get; }

    /// <summary>Gets draw offset X in pixels (tile-relative, from FIELDSTG_drawActor).</summary>
    public int OffsetX { get; }

    /// <summary>Gets draw offset Y in pixels (tile-relative, from FIELDSTG_drawActor).</summary>
    public int OffsetY { get; }

    /// <summary>Initializes a new instance of the <see cref="PlayerAnimRecord"/> class.</summary>
    /// <param name="duration">Frame duration in ticks.</param>
    /// <param name="frame">Animation frame index into the player sprite atlas.</param>
    /// <param name="offsetX">Draw offset X in pixels (tile-relative).</param>
    /// <param name="offsetY">Draw offset Y in pixels (tile-relative).</param>
    internal PlayerAnimRecord(int duration, int frame, int offsetX, int offsetY)
    {
        Duration = duration;
        Frame = frame;
        OffsetX = offsetX;
        OffsetY = offsetY;
    }
}
