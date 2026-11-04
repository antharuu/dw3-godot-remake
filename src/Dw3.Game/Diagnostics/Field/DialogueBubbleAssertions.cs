using Dw3Production.Conversion;
using Dw3Production.Logging;
using Dw3Production.Presentation;
using Godot;
using static Dw3Production.Diagnostics.Field.ProbeRequirement;

namespace Dw3Production.Diagnostics.Field;

/// <summary>
/// Checks with real framebuffer pixels that an open talk box looks like the game's: a light-blue
/// panel with an opaque body and textured borders, opaque edge sprites, a corner sprite, the
/// advance arrow, the name window, dark-blue text, and a box anchored at the speaker rather than
/// at the bottom of the screen.
/// </summary>
internal sealed class DialogueBubbleAssertions
{
    /// <summary>Frame id of the advance arrow sprite.</summary>
    private const int ArrowFrame = 7;

    /// <summary>Frame id of the left edge sprite.</summary>
    private const int LeftEdgeFrame = 0;

    /// <summary>Greatest distance in viewport pixels between the box and the speaker; the old fixed bottom panel was much farther.</summary>
    private const float MaxAnchorDistance = 150f;

    private readonly IDialogueProbeHost _host;
    private readonly DialogueFrameData _frames;
    private readonly IReadOnlyDictionary<int, Texture2D> _textures;

    /// <summary>Initializes a new instance of the <see cref="DialogueBubbleAssertions"/> class.</summary>
    /// <param name="host">Running game with an open conversation.</param>
    /// <exception cref="InvalidOperationException">The talk-box sprites were not loaded.</exception>
    public DialogueBubbleAssertions(IDialogueProbeHost host)
    {
        _host = host;
        _frames = host.DialogueFrames ?? throw new InvalidOperationException("frame data unavailable");
        _textures = host.FrameTextures ?? throw new InvalidOperationException("frame textures unavailable");
    }

    /// <summary>Checks the open talk box of a speaker.</summary>
    /// <param name="before">Capture of the same view without the talk box.</param>
    /// <param name="after">Capture with the talk box fully revealed.</param>
    /// <param name="actor">Speaker.</param>
    /// <exception cref="InvalidOperationException">A part of the talk box is missing or wrong.</exception>
    public void Check(Image before, Image after, DialogueActor actor)
    {
        var (boxOrigin, type) = DialogueBubbleLayout.Place(actor, Camera());
        var bodyBlue = CheckPanel(before, after);
        CheckSprites(before, after, boxOrigin, type, bodyBlue);
        var textBlue = DialogueBubblePixels.CountDarkBlue(after, _host.DialogueRenderer.BoxScreenRect);
        Require(textBlue > 40, $"dialogue text rendered too few dark-blue glyph pixels text_blue={textBlue} rect={_host.DialogueRenderer.BoxScreenRect}");
        CheckAnchor(actor, type);
    }

    /// <summary>Maps a source screen point (320x240) to the 1280x720 viewport: 3x, shifted 160 pixels right.</summary>
    /// <param name="source">Point in source screen pixels.</param>
    /// <returns>The viewport point.</returns>
    private static Vector2 SourceToViewport(Vector2 source) => new((source.X * 3f) + 160f, source.Y * 3f);

    /// <summary>Returns the field camera centre for the current player position.</summary>
    /// <returns>The camera centre in world units.</returns>
    private Vector2 Camera() =>
        CameraController.PositionFor(_host.CurrentZone.Zone.Width, _host.CurrentZone.Zone.Height, new Vector2(_host.State.Position.X, _host.State.Position.Y));

    /// <summary>
    /// Checks the panel: enough changed and light-blue pixels, an opaque body over the middle of
    /// the box, and a 12x62 panel texture whose top line (row 13) and bottom band (row 60) differ
    /// from its body (row 30), so its gradient was not flattened.
    /// </summary>
    /// <param name="before">Capture without the talk box.</param>
    /// <param name="after">Capture with the talk box.</param>
    /// <returns>The number of opaque body pixels over the middle of the box.</returns>
    private int CheckPanel(Image before, Image after)
    {
        var panelRect = _host.DialogueRenderer.BoxScreenRect;
        var panelChanged = DialogueBubblePixels.CountChanged(before, after, panelRect);
        Require(panelChanged > 400, $"panel rendered too few changed pixels panel_changed={panelChanged}");
        var panelBlue = DialogueBubblePixels.CountLightBlue(after, panelRect);
        Require(panelBlue > 400, $"panel rendered too few light-blue pixels panel_blue={panelBlue} rect={panelRect}");
        QuietLog.Info($"dialogue panel PASS light_blue={panelBlue} rect={panelRect}");
        var middleRect = new Rect2(
            new Vector2(panelRect.Position.X + (panelRect.Size.X * 0.25f), panelRect.Position.Y + (panelRect.Size.Y * 0.15f)),
            new Vector2(panelRect.Size.X * 0.5f, panelRect.Size.Y * 0.7f));
        var bodyBlue = DialogueBubblePixels.CountBodyBlue(after, middleRect);
        Require(bodyBlue > 4000, $"panel middle not covered by the opaque body body_blue={bodyBlue} rect={middleRect}");
        var panelImage = _host.PanelTexture.GetImage() ?? throw new InvalidOperationException("panel texture image unavailable");
        try
        {
            Require(!panelImage.IsEmpty(), "panel texture image unavailable");
            Require(
                ProbePixels.DifferentRgb(panelImage.GetPixel(0, 30), panelImage.GetPixel(0, 13)),
                $"panel texture flattened at the top line body={panelImage.GetPixel(0, 30)} top={panelImage.GetPixel(0, 13)}");
            Require(
                ProbePixels.DifferentRgb(panelImage.GetPixel(0, 30), panelImage.GetPixel(0, 60)),
                $"panel texture flattened at the bottom line body={panelImage.GetPixel(0, 30)} bottom={panelImage.GetPixel(0, 60)}");
        }
        finally
        {
            panelImage.Dispose();
        }

        return bodyBlue;
    }

    /// <summary>
    /// Checks the sprites of the box. The left edge sits over the scene, so it shows whether the
    /// edges are drawn opaque: the semi-transparent flag only blends texels already marked
    /// half-transparent in the decoded PNG, so almost every opaque texel must render pure. Then
    /// the corner sprite, the arrow and the name window must each have changed pixels.
    /// </summary>
    /// <param name="before">Capture without the talk box.</param>
    /// <param name="after">Capture with the talk box.</param>
    /// <param name="boxOrigin">Box origin in source screen pixels.</param>
    /// <param name="type">Box type, 0..3.</param>
    /// <param name="bodyBlue">Opaque body pixel count of the panel check, reported with the edges.</param>
    private void CheckSprites(Image before, Image after, Vector2 boxOrigin, int type, int bodyBlue)
    {
        var layout = _frames.Layouts[type];
        var width = _host.DialogueBoxWidth;
        var widthOffset = type < 2 ? width : 0;
        var leftTopLeft = SourceToViewport(boxOrigin + layout.Sprite0Pos - new Vector2(widthOffset, 0));
        var leftPure = DialogueBubblePixels.CountSpriteOpaquePure(before, after, _textures[LeftEdgeFrame], leftTopLeft);
        var leftSemi = DialogueBubblePixels.CountSpriteSemiBlended(before, after, _textures[LeftEdgeFrame], leftTopLeft);
        Require(leftPure > 400, $"left edge rendered too few opaque texels left_pure={leftPure} left_semi={leftSemi}");
        Require(leftSemi < leftPure, $"left edge rendered semi-transparent left_semi={leftSemi} >= left_pure={leftPure}");
        QuietLog.Info($"dialogue 9-patch PASS body_blue={bodyBlue} left_pure={leftPure} left_semi={leftSemi} rect={_host.DialogueRenderer.BoxScreenRect}");

        var cornerChanged = DialogueBubblePixels.CountSpriteOpaqueChanged(before, after, _textures[layout.Sprite], SourceToViewport(boxOrigin + layout.SpritePos));
        Require(cornerChanged > 15, $"corner sprite rendered too few opaque pixels corner_changed={cornerChanged}");
        var arrowX = type is 2 or 3 ? width - 14 : layout.Arrow.X;
        var arrowChanged = DialogueBubblePixels.CountSpriteOpaqueChanged(before, after, _textures[ArrowFrame], SourceToViewport(boxOrigin + new Vector2(arrowX, layout.Arrow.Y)));
        Require(arrowChanged > 6, $"arrow rendered too few opaque pixels arrow_changed={arrowChanged}");
        var nameOrigin = SourceToViewport(new Vector2(boxOrigin.X + layout.NameX - widthOffset, boxOrigin.Y + layout.NameY));
        var nameChanged = DialogueBubblePixels.CountChanged(before, after, new Rect2(nameOrigin, new Vector2(width * 3f, 13f * 3f)));
        Require(nameChanged > 6, $"name window rendered too few pixels name_changed={nameChanged}");
    }

    /// <summary>
    /// Checks that the box sits just above the speaker's head (types 0 and 2) or just below its
    /// feet (types 1 and 3, flipped near a screen edge), within <see cref="MaxAnchorDistance"/>.
    /// </summary>
    /// <param name="actor">Speaker.</param>
    /// <param name="type">Box type, 0..3.</param>
    private void CheckAnchor(DialogueActor actor, int type)
    {
        var actorScreenY = ((actor.Y - Camera().Y) * 3f) + 360f;
        var rect = _host.DialogueRenderer.BoxScreenRect;
        var distanceToActor = type is 0 or 2 ? actorScreenY - rect.End.Y : rect.Position.Y - actorScreenY;
        Require(
            distanceToActor >= 0f && distanceToActor < MaxAnchorDistance,
            $"bubble is not anchored to the actor distance={distanceToActor:0} actor_y={actorScreenY:0} type={type} rect={rect}");
    }
}
