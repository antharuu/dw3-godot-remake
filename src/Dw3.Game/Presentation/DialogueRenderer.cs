using Godot;
using Dw3Production.Conversion;

namespace Dw3Production.Presentation;

/// <summary>
/// Draws the PSX talk box (speech bubble) beside the speaking actor's head: the corner sprite,
/// the two edge sprites, the semi-transparent light-blue panel, the speaker-name window and the text
/// window (three lines, typed out progressively), plus a blinking "next" arrow while the box
/// waits for a Cross advance. Everything is laid out in source PSX screen pixels and mapped to
/// the 1280x720 viewport by the shared 3x world scale, so the bubble sits where the original
/// did. No sprite, colour or position is invented: the frames, layouts and panel come from
/// the disc-decoded cache, and the name/text glyphs from the font atlas.
/// </summary>
public sealed partial class DialogueRenderer : IDisposable
{
    // The source screen (PSX 320x240) maps to our 1280x720 viewport by S = 3*s + (160, 0): the
    // world is drawn at 3x and the 16:9 view shows 160 extra source pixels on each horizontal
    // side (centred). The box node draws in source coordinates; its Scale+Position apply that.
    private const float WorldScale = 3f;
    private const float SourceCenterX = 160f;
    private const float LineHeight = 14f; // FONT_STYLES[1].lineHeight (the decoded font metrics)
    private const float BoxHeight = 0x3E; // 62 source pixels, three lines

    private readonly DialogueBoxNode _box;

    /// <summary>
    /// Initializes a new instance of the <see cref="DialogueRenderer"/> class.
    /// Creates a CanvasLayer at layer 200 containing the dialogue box node,
    /// scaled to 3× and horizontally centred at the source screen's 160-pixel midpoint.
    /// </summary>
    /// <param name="root">The Godot node that receives the canvas layer containing the dialogue box.</param>
    public DialogueRenderer(Node root)
    {
        var layer = new CanvasLayer { Layer = 200 };
        _box = new DialogueBoxNode { Scale = new Vector2(WorldScale, WorldScale), Position = new Vector2(SourceCenterX, 0f) };
        layer.AddChild(_box);
        root.AddChild(layer);
    }

    /// <summary>Releases Godot resources owned by this renderer.</summary>
    public void Dispose() => _box.Dispose();

    /// <summary>Supplies the decoded font metrics and atlas pages used for dialogue glyphs.</summary>
    /// <param name="font">Decoded metrics for glyph and icon placement.</param>
    /// <param name="page0">First font atlas page.</param>
    /// <param name="page1">Second font atlas page.</param>
    public void SetFont(FontData font, Texture2D page0, Texture2D page1) => _box.SetFont(font, page0, page1);

    /// <summary>Supplies dialogue frame layouts, sprite textures, and the panel texture.</summary>
    /// <param name="frames">Decoded frame layouts.</param>
    /// <param name="textures">Textures indexed by frame identifier.</param>
    /// <param name="panelTexture">Texture stretched behind each dialogue box.</param>
    public void SetFrames(DialogueFrameData frames, IReadOnlyDictionary<int, Texture2D> textures, Texture2D panelTexture) => _box.SetFrames(frames, textures, panelTexture);

    /// <summary>Pins the arrow brightness for a deterministic offscreen probe capture.</summary>
    /// <param name="frame">Probe frame selecting the brightness, or null for live animation.</param>
    public void SetProbeArrowBrightness(int? frame) => _box.SetProbeArrowBrightness(frame);

    /// <summary>Maps a deterministic probe frame to the four arrow brightness states.</summary>
    /// <param name="frame">Non-negative probe frame.</param>
    /// <returns>The zero-based arrow brightness state.</returns>
    public static int ProbeArrowBrightnessIndex(int frame)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(frame);
        return frame % 4;
    }

    /// <summary>Gets a value indicating whether true when the current page contains at least one real glyph or icon draw.</summary>
    public bool HasGlyphPixels => _box.GlyphCount > 0 || _box.IconCount > 0;

    /// <summary>Gets the number of text glyphs were placed for the current page.</summary>
    public int GlyphCount => _box.GlyphCount;

    /// <summary>Gets the number of inline icons were placed for the current page.</summary>
    public int IconCount => _box.IconCount;

    /// <summary>Gets a value indicating whether true when the speaker name window carries real glyph pixels.</summary>
    public bool HasNamePixels => _box.NameGlyphCount > 0;

    /// <summary>Gets the number of glyphs were placed in the speaker-name window.</summary>
    public int NameGlyphCount => _box.NameGlyphCount;

    /// <summary>Gets a value indicating whether true when the corner/edge sprites and the panel were laid out this frame.</summary>
    public bool HasFramePixels => _box.FrameRendered;

    /// <summary>Gets a value indicating whether true when the blinking "next" arrow is visible this frame.</summary>
    public bool HasArrowPixels => _box.ArrowRendered;

    /// <summary>Gets the layout type selected for the current dialogue box.</summary>
    public int BoxType => _box.Type;

    /// <summary>Gets the panel rectangle in viewport pixels (the probe checks the bubble sits at the actor, not the bottom).</summary>
    public Rect2 BoxScreenRect => _box.BoxScreenRect;

    /// <summary>Updates the dialogue box with the supplied view or clears it when absent.</summary>
    /// <param name="view">Dialogue content and layout state to render.</param>
    public void Present(DialogueView? view) => _box.SetView(view);

    /// <summary>Draws the frame, panel, and glyph runs for one dialogue box.</summary>
    private sealed partial class DialogueBoxNode : Node2D
    {
        private static readonly float[] ArrowBrightness = [1f, 0.7f, 0.45f, 0.25f];
        private const ulong ArrowBlinkMs = 100; // the source's 4 arrow CLUTs, ~6 ticks each

        private FontData? _font;
        private Texture2D? _page0;
        private Texture2D? _page1;
        private DialogueFrameData? _frames;
        private IReadOnlyDictionary<int, Texture2D>? _frameTextures;
        private Texture2D? _panelTexture;
        private readonly List<GlyphDraw> _textDraws = [];
        private readonly List<GlyphDraw> _nameDraws = [];
        private TalkBoxLayout _layout = null!;
        private int _type;
        private int _boxWidth;
        private Vector2 _boxOrigin;
        private Vector2 _arrowPosition;
        private Rect2 _panelRect;
        private bool _showArrow;
        private bool _visible;
        private int? _probeArrowBrightness;

        /// <summary>Gets the number of text glyphs placed during the latest layout pass.</summary>
        public int GlyphCount { get; private set; }

        /// <summary>Gets the number of inline icons placed during the latest layout pass.</summary>
        public int IconCount { get; private set; }

        /// <summary>Gets the number of speaker-name glyphs placed during the latest layout pass.</summary>
        public int NameGlyphCount { get; private set; }

        /// <summary>Gets a value indicating whether frame sprites and the panel were prepared for drawing.</summary>
        public bool FrameRendered { get; private set; }

        /// <summary>Gets a value indicating whether the advance arrow was prepared for drawing.</summary>
        public bool ArrowRendered { get; private set; }

        /// <summary>Gets the active dialogue layout type.</summary>
        public int Type => _type;

        /// <summary>Gets the dialogue panel rectangle in viewport coordinates.</summary>
        public Rect2 BoxScreenRect { get; private set; }

        /// <summary>Stores the glyph metrics and atlas pages used by drawing.</summary>
        /// <param name="font">Decoded glyph and icon metrics.</param>
        /// <param name="page0">First atlas page.</param>
        /// <param name="page1">Second atlas page.</param>
        public void SetFont(FontData font, Texture2D page0, Texture2D page1)
        {
            _font = font;
            _page0 = page0;
            _page1 = page1;
        }

        /// <summary>Stores dialogue layout data and frame textures.</summary>
        /// <param name="frames">Decoded dialogue frame definitions.</param>
        /// <param name="textures">Frame textures indexed by identifier.</param>
        /// <param name="panelTexture">Texture used for the box panel.</param>
        public void SetFrames(DialogueFrameData frames, IReadOnlyDictionary<int, Texture2D> textures, Texture2D panelTexture)
        {
            _frames = frames;
            _frameTextures = textures;
            _panelTexture = panelTexture;
        }

        /// <summary>Selects a deterministic arrow brightness state for probe captures.</summary>
        /// <param name="frame">Probe frame that chooses the brightness state, or null for live animation.</param>
        public void SetProbeArrowBrightness(int? frame)
        {
            _probeArrowBrightness = frame is null ? null : DialogueRenderer.ProbeArrowBrightnessIndex(frame.Value);
            QueueRedraw();
        }

        /// <summary>Rebuilds draw state from a dialogue view or hides the node when absent.</summary>
        /// <param name="view">Dialogue view to lay out.</param>
        public void SetView(DialogueView? view)
        {
            ResetDrawState();
            if (view is not { } value || _font is null || _page0 is null || _page1 is null || _frames is null || _frameTextures is null)
            {
                QueueRedraw();
                return;
            }

            ApplyView(value);
            FrameRendered = true;
            ArrowRendered = _showArrow;
            QueueRedraw();
        }

        /// <summary>Clears layout results before processing another dialogue view.</summary>
        private void ResetDrawState()
        {
            _textDraws.Clear();
            _nameDraws.Clear();
            GlyphCount = 0;
            IconCount = 0;
            NameGlyphCount = 0;
            FrameRendered = false;
            ArrowRendered = false;
            _visible = false;
            BoxScreenRect = default;
        }

        /// <summary>Translates dialogue data into panel, frame, arrow, and glyph placements.</summary>
        /// <param name="value">Dialogue view to translate.</param>
        private void ApplyView(DialogueView value)
        {
            _visible = true;
            _type = value.Type;
            _layout = _frames!.Layouts[value.Type];
            _boxWidth = value.BoxWidth;
            _boxOrigin = value.BoxOrigin;

            // drawTalkBoxFrame: the panel is shifted left by w for the left-side types (0/1).
            var widthOffset = _type < 2 ? _boxWidth : 0;
            var panelX = _boxOrigin.X + _layout.Panel.X - widthOffset;
            var panelY = _boxOrigin.Y + _layout.Panel.Y;
            _panelRect = new Rect2(panelX, panelY, _boxWidth, BoxHeight);
            BoxScreenRect = new Rect2(
                panelX * WorldScale + SourceCenterX,
                panelY * WorldScale,
                _boxWidth * WorldScale,
                BoxHeight * WorldScale);

            // Name window (createTextWindow style 2, 3 lines): the speaker name between the two
            // 02 07 codes, shown instantly and for the whole conversation. A player-name field
            // (control 9) has no glyphs to draw, so the name window stays empty.
            if (value.SpeakerName.Count > 0 && !value.SpeakerIsPlayer)
            {
                var nameX = _boxOrigin.X + _layout.NameX - widthOffset;
                var nameY = _boxOrigin.Y + _layout.NameY;
                LayoutTokens(_nameDraws, value.SpeakerName, nameX, nameY, nameWindow: true, revealCount: -1);
            }

            // Text window (createTextWindow style 1, 3 lines): typed out to RevealCount.
            var textX = _boxOrigin.X + _layout.TextX - widthOffset;
            var textY = _boxOrigin.Y + _layout.TextY;
            LayoutTokens(_textDraws, value.Page, textX, textY, nameWindow: false, revealCount: value.RevealCount);

            // drawTalkBoxArrow: x = w - 14 for the right-side types (2/3), else arrow.vx.
            _showArrow = value.ShowArrow;
            var arrowX = _type is 2 or 3 ? _boxWidth - 14 : _layout.Arrow.X;
            _arrowPosition = new Vector2(_boxOrigin.X + arrowX, _boxOrigin.Y + _layout.Arrow.Y);
        }

        /// <summary>Lays out a token run from left to right in source pixels.</summary>
        /// <param name="target">Draw list receiving the resolved glyph placements.</param>
        /// <param name="tokens">Name or dialogue-page tokens to lay out.</param>
        /// <param name="originX">Source x coordinate of the run origin.</param>
        /// <param name="originY">Source y coordinate of the run origin.</param>
        /// <param name="nameWindow">Whether the run belongs to the speaker-name window.</param>
        /// <param name="revealCount">Maximum token count to reveal, or negative one for all tokens.</param>
        private void LayoutTokens(List<GlyphDraw> target, IReadOnlyList<TextToken> tokens, float originX, float originY, bool nameWindow, int revealCount)
        {
            var cursorX = 0f;
            var cursorY = 0f;
            var index = 0;
            foreach (var token in tokens)
            {
                if (revealCount >= 0 && index >= revealCount)
                {
                    break;
                }

                index++;
                if (token.IsControl)
                {
                    if (token.Command == 1)
                    {
                        cursorX = 0f;
                        cursorY += LineHeight;
                    }

                    continue;
                }

                var metric = ResolveGlyphMetric(token, nameWindow);
                if (metric is not { } glyph)
                {
                    continue;
                }

                target.Add(new GlyphDraw(
                    glyph.Page,
                    new Rect2(originX + cursorX + glyph.Dx, originY + cursorY + glyph.Dy, glyph.W, glyph.H),
                    new Rect2(glyph.U, glyph.V, glyph.W, glyph.H)));
                cursorX += glyph.Advance + glyph.Dx;
            }
        }

        /// <summary>Resolves one token to its drawable metric and updates placement counters.</summary>
        /// <param name="token">Token to resolve.</param>
        /// <param name="nameWindow">Gets a value indicating whether the token belongs to the speaker-name window.</param>
        /// <returns>The matching glyph or icon metric, when available.</returns>
        private GlyphMetric? ResolveGlyphMetric(TextToken token, bool nameWindow)
        {
            if (token.IsGlyph)
            {
                if (!_font!.Glyphs.TryGetValue(token.Code, out var glyphMetric))
                {
                    return null;
                }

                if (nameWindow)
                {
                    NameGlyphCount++;
                }
                else
                {
                    GlyphCount++;
                }

                return glyphMetric;
            }

            if (token.IsIcon)
            {
                if (!_font!.Icons.TryGetValue(token.Code, out var iconMetric))
                {
                    return null;
                }

                if (!nameWindow)
                {
                    IconCount++;
                }

                return iconMetric;
            }

            return null;
        }

        /// <summary>Draws the active panel, frame pieces, arrow, and laid-out glyphs.</summary>
        public override void _Draw()
        {
            if (!_visible || _frames is null || _frameTextures is null || _page0 is null || _page1 is null || _panelTexture is null)
            {
                return;
            }

            var widthOffset = _type < 2 ? _boxWidth : 0;

            // Semi-transparent panel (POLY_FT4): the 12x62 light-blue texture stretched across
            // the box width. The body texels are opaque (setSemiTrans only blends the STP-marked
            // texels of a textured primitive), so the panel covers the scene; the STP border rows
            // carry their alpha-128 semi-transparency in the decoded PNG.
            DrawTextureRectRegion(_panelTexture, _panelRect, new Rect2(Vector2.Zero, _panelTexture.GetSize()), new Color(1f, 1f, 1f, 1f));

            // The four box parts (drawTalkBoxFrame): corner sprite, left edge, right edge. The
            // edge sprites' semi-transparency comes only from their STP-marked texels (already
            // baked into the decoded PNG alpha): setSemiTrans on a textured primitive blends only
            // the STP texels, non-STP texels stay opaque. So the edges are drawn at full alpha and
            // their STP texels blend through the PNG's alpha-128.
            var opaque = new Color(1f, 1f, 1f, 1f);
            DrawFrame(_layout.Sprite, _boxOrigin + _layout.SpritePos, opaque);                                   // corner
            DrawFrame(0, _boxOrigin + _layout.Sprite0Pos - new Vector2(widthOffset, 0), opaque);                // left edge
            if (_type < 2)
            {
                DrawFrame(2, _boxOrigin + _layout.Sprite2Pos, opaque);                                          // right edge
            }
            else
            {
                DrawFrame(2, _boxOrigin + new Vector2(_boxWidth - 14, _layout.Sprite2Pos.Y), opaque);
            }

            // Blinking "next" arrow (frame 7), opaque, cycling its four CLUT brightness levels.
            if (_showArrow)
            {
                var blink = _probeArrowBrightness ?? (int)((Time.GetTicksMsec() / ArrowBlinkMs) % (ulong)ArrowBrightness.Length);
                var brightness = ArrowBrightness[blink];
                DrawFrame(7, _arrowPosition, new Color(brightness, brightness, brightness, 1f));
            }

            foreach (var draw in _nameDraws)
            {
                DrawTextureRectRegion(draw.Page == 0 ? _page0 : _page1, draw.Target, draw.Source, opaque);
            }

            foreach (var draw in _textDraws)
            {
                DrawTextureRectRegion(draw.Page == 0 ? _page0 : _page1, draw.Target, draw.Source, opaque);
            }
        }

        /// <summary>Draws one frame texture at a source-coordinate position.</summary>
        /// <param name="frameId">Identifier of the frame texture.</param>
        /// <param name="position">Destination position in source coordinates.</param>
        /// <param name="modulate">Colour multiplier applied while drawing.</param>
        private void DrawFrame(int frameId, Vector2 position, Color modulate)
        {
            if (!_frameTextures!.TryGetValue(frameId, out var texture))
            {
                return;
            }

            var size = texture.GetSize();
            DrawTextureRectRegion(texture, new Rect2(position, size), new Rect2(Vector2.Zero, size), modulate);
        }

        /// <summary>Describes one atlas region and destination rectangle for a glyph or icon.</summary>
        private readonly record struct GlyphDraw(int Page, Rect2 Target, Rect2 Source);
    }
}
