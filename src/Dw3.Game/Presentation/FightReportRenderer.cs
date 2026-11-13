using System.Globalization;
using Dw3Production.Conversion;
using Dw3Production.Simulation;
using Godot;

namespace Dw3Production.Presentation;

/// <summary>STFGTREP's independent 320x240 report layer. It never invents sprites: every frame
/// comes from the report cache and keeps SpriteDrawer's decoded part offset.</summary>
public sealed partial class FightReportRenderer : IDisposable
{
    private readonly CanvasLayer _layer;

    private readonly ReportControl _control;

    private readonly ColorRect _fader;

    /// <summary>
    /// Initializes a new instance of the <see cref="FightReportRenderer"/> class.
    /// Creates a canvas layer at depth 350, a report control scaled and positioned
    /// by the shared HUD layout, and a full-rect colour fade overlay.
    /// </summary>
    /// <param name="root">Parent Godot node that receives the report canvas layer.</param>
    public FightReportRenderer(Node root)
    {
        _layer = new CanvasLayer { Layer = 350, Visible = false };
        root.AddChild(_layer);
        _control = new ReportControl { Scale = new Vector2(BattleHudLayout.Scale, BattleHudLayout.Scale), Position = new Vector2(BattleHudLayout.OffsetX, BattleHudLayout.OffsetY) };
        _layer.AddChild(_control);
        _fader = new ColorRect { Color = new Color(0, 0, 0, 0), MouseFilter = Control.MouseFilterEnum.Ignore };
        _fader.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _layer.AddChild(_fader);
    }

    /// <summary>Gets a value indicating whether the report layer is visible.</summary>
    public bool Visible => _layer.Visible;

    /// <summary>Releases Godot resources owned by this renderer.</summary>
    public void Dispose()
    {
        _fader.Dispose();
        _control.Dispose();
        _layer.Dispose();
    }

    /// <summary>Supplies decoded report sprites and font metrics.</summary>
    /// <param name="sprites">Decoded report sprite frames and animation data.</param>
    /// <param name="font">Decoded font metrics.</param>
    public void Configure(BattleReportSprites sprites, FontData font) => _control.Configure(sprites, font);

    /// <summary>Makes the report layer visible.</summary>
    public void Show() => _layer.Visible = true;

    /// <summary>Hides the report layer.</summary>
    public void Hide() => _layer.Visible = false;

    /// <summary>Updates report sprites, text, and fade state from a report view.</summary>
    /// <param name="view">Current report presentation state.</param>
    /// <param name="text">Catalog used for report messages.</param>
    /// <param name="names">Fighter names indexed by fighter identifier.</param>
    /// <param name="itemNames">FSITMNAM item names indexed by item identifier.</param>
    public void Present(
        FightReportView view,
        BattleTextCatalog text,
        IReadOnlyDictionary<int, BattleTextLine> names,
        IReadOnlyDictionary<int, BattleTextLine> itemNames)
    {
        _control.SetView(view, text, names, itemNames);
        _fader.Color = new Color(0, 0, 0, Mathf.Clamp(view.ExitFade / 4096f, 0, 1));
    }

    /// <summary>
    /// Draws the report in three ordered passes: the opaque sprites on this control, then the semi-transparent sprite texels
    /// on an additive layer, as the original's semi-transparent sprites add to the screen, then every text window on top.
    /// </summary>
    private sealed partial class ReportControl : Control
    {
        private readonly FightReportSpriteSheet _sheet = new();

        private readonly FightReportLayer _additive;

        private readonly FightReportLayer _textLayer;

        private readonly PsxTextPainter _text;

        private ImageTexture[]? _arrow;

        private Vector2 _arrowOffset;

        private IReadOnlyList<int[]> _animations = [];

        private FightReportView? _view;

        private BattleTextCatalog? _catalog;

        private FightReportMessageLineResolver? _messageResolver;

        private IReadOnlyDictionary<int, BattleTextLine> _names = new Dictionary<int, BattleTextLine>();

        private IReadOnlyDictionary<int, BattleTextLine> _itemNames = new Dictionary<int, BattleTextLine>();

        /// <summary>Initializes a new instance of the <see cref="ReportControl"/> class.</summary>
        public ReportControl()
        {
            MouseFilter = MouseFilterEnum.Ignore;
            _additive = new FightReportLayer { Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add } };
            _additive.Painter = DrawAdditivePass;
            AddChild(_additive);
            _textLayer = new FightReportLayer();
            _textLayer.Painter = DrawTextPass;
            AddChild(_textLayer);
            _text = new PsxTextPainter(_textLayer);
        }

        /// <summary>Loads report frame textures and text metrics.</summary>
        /// <param name="sprites">Decoded report sprites.</param>
        /// <param name="font">Decoded font metrics.</param>
        public void Configure(BattleReportSprites sprites, FontData font)
        {
            _sheet.Load(sprites);
            var arrowImages = sprites.Arrow.ClutRows.Select(Image.LoadFromFile).ToArray();
            if (arrowImages.Any(image => image is null || image.IsEmpty()))
            {
                throw new CacheResourceException("CACHE_RESOURCE_UNAVAILABLE", "battle report arrow unreadable");
            }

            _arrow = [.. arrowImages.Select(image => ImageTexture.CreateFromImage(image))];
            _arrowOffset = new Vector2(sprites.Arrow.OffsetX, sprites.Arrow.OffsetY);
            _animations = sprites.Animations;
            _text.SetFont(font);
        }

        /// <summary>Stores report state and requests a redraw of every pass.</summary>
        /// <param name="view">Report view to render.</param>
        /// <param name="catalog">Text catalog for report messages.</param>
        /// <param name="names">Fighter names indexed by identifier.</param>
        /// <param name="itemNames">FSITMNAM item names indexed by identifier.</param>
        public void SetView(
            FightReportView view,
            BattleTextCatalog catalog,
            IReadOnlyDictionary<int, BattleTextLine> names,
            IReadOnlyDictionary<int, BattleTextLine> itemNames)
        {
            _view = view;
            _catalog = catalog;
            _names = names;
            _itemNames = itemNames;
            _messageResolver = new FightReportMessageLineResolver(catalog, ResolveFighterName, ResolveItemName);
            QueueRedraw();
            _additive.QueueRedraw();
            _textLayer.QueueRedraw();
        }

        /// <summary>Draws the opaque pass: the background, the header and footer windows and every panel's opaque sprites.</summary>
        public override void _Draw()
        {
            if (_view is not { } view || _catalog is null)
            {
                return;
            }

            // the battle report layout contract: tile sprite 0x1e at (bgScroll,bgScroll), covering the full report layer.
            for (var y = view.BgScroll - 96; y < 240; y += 96)
            {
                for (var x = view.BgScroll - 96; x < 320; x += 96)
                {
                    _sheet.DrawOpaque(this, 0x1e, new Vector2(x, y));
                }
            }

            DrawScaledFrame(0x21, new Vector2(0, 0x0f), new Vector2(view.HeaderFade / 4096f, 1), new Vector2(0, 0x18));
            DrawScaledFrame(0x1d, new Vector2(0x0b, 0xbc), new Vector2(1, view.FooterFade / 4096f), new Vector2(0xa0, 0xce));
            for (var index = 0; index < view.Partners.Count; index++)
            {
                DrawPartnerSprites(view.Partners[index], index * 50);
            }
        }

        /// <summary>Returns the portrait frame for a number of animation steps. A sequence restarts at its first -1 entry.</summary>
        /// <param name="sequence">The partner's up to seven frame ids, -1 ending it early.</param>
        /// <param name="steps">Animation steps elapsed.</param>
        /// <returns>The frame id, or null for an empty sequence.</returns>
        private static int? PortraitFrame(int[] sequence, int steps)
        {
            var length = Array.IndexOf(sequence, -1);
            length = length < 0 ? sequence.Length : length;
            return length == 0 ? null : sequence[steps % length];
        }

        /// <summary>Draws the selection cursor, the current slot's cursor and the raise's level-up markers.</summary>
        /// <param name="partner">Partner state to render.</param>
        /// <param name="y">Panel vertical origin.</param>
        /// <param name="draw">Frame drawer of the current pass.</param>
        private static void DrawPartnerMarkers(FightReportPartnerView partner, int y, Action<int, Vector2, int> draw)
        {
            if (partner.Selected)
            {
                if (partner.Raising && partner.SlotCursor >= 0)
                {
                    draw(0x25, new Vector2(0x87, y + 0x26 + (partner.SlotCursor * 15)), partner.CursorFrame);
                }

                draw(0x24, new Vector2(0x11, y + 0x26), partner.CursorFrame);
            }

            if (!partner.Raising)
            {
                return;
            }

            for (var slot = 0; slot < partner.Slots.Count; slot++)
            {
                if (partner.Slots[slot].Blinking)
                {
                    draw(0x23, new Vector2(0xEF, y + 0x23 + (slot * 15)), partner.Slots[slot].BlinkFrame);
                }
            }

            if (partner.LevelBlinking)
            {
                draw(0x22, new Vector2(0x63, y + 0x2B), partner.LevelBlinkFrame);
            }
        }

        /// <summary>Draws the semi-transparent texels of every panel's cursors and blink markers.</summary>
        /// <param name="layer">Additive layer.</param>
        private void DrawAdditivePass(FightReportLayer layer)
        {
            if (_view is not { } view)
            {
                return;
            }

            for (var index = 0; index < view.Partners.Count; index++)
            {
                DrawPartnerMarkers(view.Partners[index], index * 50, (frame, position, row) => _sheet.DrawAdditive(layer, frame, position, row));
            }
        }

        /// <summary>Draws every text window and the message arrow over the sprites.</summary>
        /// <param name="layer">Text layer.</param>
        private void DrawTextPass(FightReportLayer layer)
        {
            if (_view is not { } view || _catalog is null)
            {
                return;
            }

            for (var index = 0; index < view.Partners.Count; index++)
            {
                DrawPartnerText(view.Partners[index], index * 50);
            }

            DrawHeaderExp(view.HeaderExp);
            DrawMessage(layer, view.Message);
        }

        /// <summary>Draws the selected partner's experience and its FGTRPT label in the header windows.</summary>
        /// <param name="exp">Experience shown while the header carries it, or null.</param>
        private void DrawHeaderExp(int? exp)
        {
            if (exp is not { } value || _catalog is null)
            {
                return;
            }

            _text.DrawWindow(3, 0x42, 0x17, value.ToString(CultureInfo.InvariantCulture), true, 0);
            _text.DrawLine(3, 0x45, 0x17, _catalog.Report("expLabel"), false, 0);
        }

        /// <summary>
        /// Draws one partner panel's opaque sprites as the original's drawPartner does. Its sprites share one
        /// ordering-table depth, where a later call is drawn behind an earlier one, so they are painted here in reverse
        /// call order: the three slot bars, the panel frame, the portrait, then the slot and selection cursors and the
        /// raise's blink markers.
        /// </summary>
        /// <param name="partner">Partner state to render.</param>
        /// <param name="y">Panel vertical origin.</param>
        private void DrawPartnerSprites(FightReportPartnerView partner, int y)
        {
            if (partner.Fade > 0)
            {
                DrawPanelSprites(partner, y, partner.Fade / 4096f);
            }

            DrawPartnerMarkers(partner, y, (frame, position, row) => _sheet.DrawOpaque(this, frame, position, row));
        }

        /// <summary>Draws one panel's text windows once it is fully shown.</summary>
        /// <param name="partner">Partner state to render.</param>
        /// <param name="y">Panel vertical origin.</param>
        private void DrawPartnerText(FightReportPartnerView partner, int y)
        {
            if (!partner.TextVisible)
            {
                return;
            }

            if (_names.TryGetValue(partner.FighterId, out var name))
            {
                _text.DrawLine(1, 0x3D, y + 0x28, name, false, 0);
            }

            DrawPartnerNumbers(partner, y);
            DrawSlots(partner, y);
        }

        /// <summary>Draws the slot bars, the panel frame and the portrait, squeezed horizontally while the panel fades.</summary>
        /// <param name="partner">Partner state to render.</param>
        /// <param name="y">Panel vertical origin.</param>
        /// <param name="scale">Horizontal fade scale, 1 when fully shown.</param>
        private void DrawPanelSprites(FightReportPartnerView partner, int y, float scale)
        {
            for (var slot = 0; slot < partner.Slots.Count; slot++)
            {
                FightReportSlotView row = partner.Slots[slot];
                var rowY = y + (slot * 15);
                DrawSqueezedFrame(0x20, new Vector2(0x87, rowY + 0x26), scale, new Vector2(0x87, rowY + 0x2D), row.Blinking ? row.BlinkFrame : 0);
            }

            var pivot = new Vector2(0x11, y + 0x3A);
            DrawSqueezedFrame(0x1f, new Vector2(0x11, y + 0x26), scale, pivot, partner.LevelBlinking ? partner.LevelBlinkFrame : 0);
            if (partner.PartnerId >= 0 && partner.PartnerId < _animations.Count && PortraitFrame(_animations[partner.PartnerId], partner.AnimationFrame) is int frame)
            {
                DrawSqueezedFrame(frame, new Vector2(0x14, y + 0x28), scale, pivot, 0);
            }
        }

        /// <summary>Draws a frame scaled horizontally around a pivot, as the panel fade does.</summary>
        /// <param name="frame">Frame identifier.</param>
        /// <param name="position">Destination position at full scale.</param>
        /// <param name="scale">Horizontal scale.</param>
        /// <param name="pivot">Fixed point of the scale.</param>
        /// <param name="row">Colour row.</param>
        private void DrawSqueezedFrame(int frame, Vector2 position, float scale, Vector2 pivot, int row)
        {
            DrawSetTransform(new Vector2(pivot.X * (1 - scale), 0), 0, new Vector2(scale, 1));
            _sheet.DrawOpaque(this, frame, position, row);
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }

        /// <summary>Draws the source report's partner level and spaced experience windows.</summary>
        /// <param name="partner">Partner values to display.</param>
        /// <param name="y">Panel vertical origin.</param>
        private void DrawPartnerNumbers(FightReportPartnerView partner, int y)
        {
            _text.DrawWindow(3, 91, y + 56, partner.Level.ToString(CultureInfo.InvariantCulture), true, 0);
            _text.DrawWindow(3, 103, y + 69, partner.ShownExp.ToString(CultureInfo.InvariantCulture), true, 0);
            if (_catalog is not null)
            {
                // Source panel windows: the FGTRPT level label (entry 3) left of the level and the EXP label (entry 1) right of the EXP.
                _text.DrawLine(3, 0x3E, y + 0x38, _catalog.Report("levelLabel"), false, 0);
                _text.DrawLine(3, 0x6A, y + 0x45, _catalog.Report("expLabel"), false, 0);
            }
        }

        /// <summary>Draws the occupied slot rows' decoded form names and levels.</summary>
        /// <param name="partner">Partner slot values to display.</param>
        /// <param name="y">Panel vertical origin.</param>
        private void DrawSlots(FightReportPartnerView partner, int y)
        {
            for (var slotIndex = 0; slotIndex < partner.Slots.Count; slotIndex++)
            {
                FightReportSlotView slot = partner.Slots[slotIndex];
                if (slot.Visible)
                {
                    DrawSlot(slot, y + (slotIndex * 15));
                }
            }
        }

        /// <summary>Draws one equipped form's name and right-aligned level at source coordinates.</summary>
        /// <param name="slot">Slot display data.</param>
        /// <param name="y">Slot vertical origin.</param>
        private void DrawSlot(FightReportSlotView slot, int y)
        {
            if (_names.TryGetValue(slot.DigimonId, out BattleTextLine? name))
            {
                _text.DrawLine(1, 141, y + 40, name, false, 0);
            }

            if (slot.Level > 0)
            {
                _text.DrawWindow(3, 276, y + 44, slot.Level.ToString(CultureInfo.InvariantCulture), true, 0);
            }
        }

        /// <summary>Resolves one report fighter identifier without ever rendering its debug fallback.</summary>
        /// <param name="fighterId">Partner or form identifier named by a report message.</param>
        /// <returns>The extracted name line, or a missing line that the text painter keeps blank.</returns>
        private BattleTextLine ResolveFighterName(int fighterId) => _names.TryGetValue(fighterId, out var line)
            ? line
            : BattleTextLine.Missing;

        /// <summary>Resolves an item reward through its extracted FSITMNAM name without using its description text.</summary>
        /// <param name="itemId">Item identifier named by the report reward.</param>
        /// <returns>The extracted item-name line, or a missing line that the text painter keeps blank.</returns>
        private BattleTextLine ResolveItemName(int itemId) => _itemNames.TryGetValue(itemId, out var line)
            ? line
            : BattleTextLine.Missing;

        /// <summary>Draws the current report message and advance arrow.</summary>
        /// <param name="layer">Text layer.</param>
        /// <param name="message">Optional message state to render.</param>
        private void DrawMessage(FightReportLayer layer, FightReportMessage? message)
        {
            if (message is null || _messageResolver is null)
            {
                return;
            }

            BattleTextLine line = _messageResolver.Resolve(message);
            _text.DrawLine(1, 0x14, 0xc2, line, false, 0, message.TokensRevealed);
            if (message.ArrowVisible && _arrow is { Length: > 0 })
            {
                layer.DrawTexture(_arrow[message.ArrowFrame % _arrow.Length], new Vector2(0x123, 0xd0) + _arrowOffset);
            }
        }

        /// <summary>Draws a frame after applying a scale around a pivot.</summary>
        /// <param name="frame">Frame identifier to draw.</param>
        /// <param name="position">Destination position.</param>
        /// <param name="scale">Scale applied to the frame.</param>
        /// <param name="pivot">Pivot used for scaling.</param>
        private void DrawScaledFrame(int frame, Vector2 position, Vector2 scale, Vector2 pivot)
        {
            if (scale.X <= 0 || scale.Y <= 0)
            {
                return;
            }

            DrawSetTransform(position + pivot, 0, scale);
            _sheet.DrawOpaque(this, frame, -pivot);
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }
    }
}
