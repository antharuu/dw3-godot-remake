using Dw3Production.Conversion;
using Godot;

namespace Dw3Production.Diagnostics.Field;

/// <summary>
/// Compares a rendered frame with decoded NPC idle frames, alone or where they overlap a map
/// object. A horizontally flipped NPC reads its texels mirrored. Each texel maps to the centre
/// pixel of its 3x3 block for the camera of the current player position.
/// </summary>
internal sealed class NpcPixels
{
    /// <summary>Running game.</summary>
    private readonly IFieldProbeHost _host;

    /// <summary>Initializes a new instance of the <see cref="NpcPixels"/> class.</summary>
    /// <param name="host">Running game.</param>
    public NpcPixels(IFieldProbeHost host)
    {
        _host = host;
    }

    /// <summary>Counts opaque texels of the NPC frame whose block centre shows the texel colour.</summary>
    /// <param name="capture">Rendered viewport.</param>
    /// <param name="npc">NPC record with its world position and flip.</param>
    /// <param name="frame">Decoded NPC frame.</param>
    /// <returns>The number of matching texels; 0 when the frame image is unreadable.</returns>
    public int CountSourceMatches(Image capture, NpcRecord npc, NpcFrame frame)
    {
        var image = ProbePixels.TryLoad(frame.Path);
        if (image is null)
        {
            return 0;
        }

        var projection = Projection();
        var left = projection.X(npc.X + frame.OffsetX);
        var top = projection.Y(npc.Y + frame.OffsetY);
        var width = image.GetWidth();
        var matches = 0;
        foreach (var (x, y, expected) in ProbePixels.OpaqueTexels(image))
        {
            var sourceX = npc.FlipH ? width - 1 - x : x;
            var screenX = left + (sourceX * 3) + 1;
            var screenY = top + (y * 3) + 1;
            if (ProbePixels.Contains(capture, screenX, screenY) && ProbePixels.SameRgb(capture.GetPixel(screenX, screenY), expected))
            {
                matches++;
            }
        }

        return matches;
    }

    /// <summary>
    /// Counts the screen pixels where opaque texels of the NPC idle frame 0 and of the object frame 0
    /// overlap and the capture shows the colour of the sprite expected in front.
    /// </summary>
    /// <param name="capture">Rendered viewport.</param>
    /// <param name="npc">NPC record.</param>
    /// <param name="obj">Map object.</param>
    /// <param name="npcInFront"><see langword="true"/> when the NPC must cover the object.</param>
    /// <returns>The number of overlap pixels showing the expected sprite; 0 when an image is unreadable.</returns>
    public int CountObjectOverlapMatches(Image capture, NpcRecord npc, WaterObject obj, bool npcInFront)
    {
        var npcFrame = npc.IdleFrames[0];
        var objFrame = obj.Frames[0];
        var npcImage = ProbePixels.TryLoad(npcFrame.Path);
        var objImage = ProbePixels.TryLoad(objFrame.OpaquePath);
        if (npcImage is null || objImage is null)
        {
            return 0;
        }

        var projection = Projection();
        var npcLeft = projection.X(npc.X + npcFrame.OffsetX);
        var npcTop = projection.Y(npc.Y + npcFrame.OffsetY);
        var objLeft = projection.X(obj.X + objFrame.OffsetX);
        var objTop = projection.Y(obj.Y + objFrame.OffsetY);
        var matches = 0;
        foreach (var (ox, oy, objPixel) in ProbePixels.OpaqueTexels(objImage))
        {
            var screenX = objLeft + (ox * 3) + 1;
            var screenY = objTop + (oy * 3) + 1;
            if (!ProbePixels.Contains(capture, screenX, screenY, margin: 1)
                || NpcTexelAt(npcImage, npc.FlipH, (screenX - npcLeft - 1) / 3, (screenY - npcTop - 1) / 3) is not { } npcPixel)
            {
                continue;
            }

            var expected = npcInFront ? npcPixel : objPixel;
            matches += ProbePixels.SameRgb(capture.GetPixel(screenX, screenY), expected) ? 1 : 0;
        }

        return matches;
    }

    /// <summary>Returns the opaque NPC texel drawn at an unflipped sprite position, if any.</summary>
    /// <param name="npcImage">Decoded NPC frame.</param>
    /// <param name="flipH">The NPC is drawn mirrored.</param>
    /// <param name="rawX">Column inside the drawn sprite, before mirroring.</param>
    /// <param name="y">Row inside the sprite.</param>
    /// <returns>The texel colour, or <see langword="null"/> outside the sprite or on a transparent texel.</returns>
    private static Color? NpcTexelAt(Image npcImage, bool flipH, int rawX, int y)
    {
        var width = npcImage.GetWidth();
        var x = flipH ? width - 1 - rawX : rawX;
        if (x < 0 || y < 0 || x >= width || y >= npcImage.GetHeight())
        {
            return null;
        }

        var pixel = npcImage.GetPixel(x, y);
        return ProbePixels.IsOpaque(pixel) ? pixel : null;
    }

    /// <summary>Returns the block-corner projection for the current zone and player position.</summary>
    /// <returns>The projection.</returns>
    private FieldScreenProjection Projection() => FieldScreenProjection.BlockCorners(_host.CurrentZone, _host.State.Position);
}
