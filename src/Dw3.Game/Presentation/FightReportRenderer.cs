using System.Globalization;
using Godot;
using Dw3Production.Conversion;
using Dw3Production.Simulation;

namespace Dw3Production.Presentation;

/// <summary>STFGTREP's independent 320x240 report layer. It never invents sprites: every frame
/// comes from the report cache and keeps SpriteDrawer's decoded part offset.</summary>
public sealed partial class FightReportRenderer : IDisposable
{
    private readonly CanvasLayer _layer;
    private readonly ReportControl _control;
    private readonly ColorRect _fader;

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

    /// <summary>Releases Godot resources owned by this renderer.</summary>
    public void Dispose()
    {
        _fader.Dispose();
        _control.Dispose();
        _layer.Dispose();
    }

    public bool Visible => _layer.Visible;

    public void Configure(BattleReportSprites sprites, FontData font) => _control.Configure(sprites, font);

    public void Show() => _layer.Visible = true;

    public void Hide() => _layer.Visible = false;

    public void Present(FightReportView view, BattleTextCatalog text, IReadOnlyDictionary<int, BattleTextLine> names)
    {
        _control.SetView(view, text, names);
        _fader.Color = new Color(0, 0, 0, Mathf.Clamp(view.ExitFade / 4096f, 0, 1));
    }

    private sealed partial class ReportControl : Control
    {
        private readonly Dictionary<int, ImageTexture[]> _frames = [];
        private readonly Dictionary<int, Vector2> _offsets = [];
        private ImageTexture[]? _arrow;
        private Vector2 _arrowOffset;
        private IReadOnlyList<int[]> _animations = [];
        private readonly PsxTextPainter _text;
        private FightReportView? _view;
        private BattleTextCatalog? _catalog;
        private IReadOnlyDictionary<int, BattleTextLine> _names = new Dictionary<int, BattleTextLine>();

        public ReportControl()
        {
            MouseFilter = MouseFilterEnum.Ignore;
            _text = new PsxTextPainter(this);
        }

        public void Configure(BattleReportSprites sprites, FontData font)
        {
            _frames.Clear();
            _offsets.Clear();
            foreach (var (id, frame) in sprites.Frames)
            {
                var images = frame.ClutRows.Select(Image.LoadFromFile).ToArray();
                if (images.Any(image => image is null || image.IsEmpty()))
                {
                    throw new CacheResourceException("CACHE_RESOURCE_UNAVAILABLE", $"battle report frame unreadable frame={id}");
                }

                _frames[id] = [.. images.Select(image => ImageTexture.CreateFromImage(image))];
                _offsets[id] = new Vector2(frame.OffsetX, frame.OffsetY);
            }

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

        public void SetView(FightReportView view, BattleTextCatalog catalog, IReadOnlyDictionary<int, BattleTextLine> names)
        {
            _view = view;
            _catalog = catalog;
            _names = names;
            QueueRedraw();
        }

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
                    DrawFrame(0x1e, x, y);
                }
            }

            DrawScaledFrame(0x21, new Vector2(0, 0x0f), new Vector2(view.HeaderFade / 4096f, 1), new Vector2(0, 0x18));
            DrawScaledFrame(0x1d, new Vector2(0x0b, 0xbc), new Vector2(1, view.FooterFade / 4096f), new Vector2(0xa0, 0xce));
            for (var index = 0; index < view.Partners.Count; index++)
            {
                DrawPartner(view.Partners[index], index);
            }

            DrawMessage(view.Message);
        }

        private void DrawPartner(FightReportPartnerView partner, int index)
        {
            if (partner.Fade <= 0)
            {
                return;
            }

            var y = index * 50;
            var alpha = partner.Fade / 4096f;
            DrawFrame(0x1f, 16, y + 38, 0, new Color(1, 1, 1, alpha));
            DrawFrame(0x22, 55, y + 42, 0, new Color(1, 1, 1, alpha));
            DrawFrame(0x22, 135, y + 52, 0, new Color(1, 1, 1, alpha));
            DrawFrame(0x22, 135, y + 67, 0, new Color(1, 1, 1, alpha));
            DrawFrame(0x22, 135, y + 82, 0, new Color(1, 1, 1, alpha));
            if (partner.PartnerId >= 0 && partner.PartnerId < _animations.Count)
            {
                var sequence = _animations[partner.PartnerId];
                var frameIndex = sequence[Math.Min(partner.AnimationFrame % sequence.Length, sequence.Length - 1)];
                if (frameIndex >= 0)
                {
                    DrawFrame(frameIndex, 19, y + 43, 0, new Color(1, 1, 1, alpha));
                }
            }

            if (_names.TryGetValue(partner.FighterId, out var name))
            {
                _text.DrawLine(1, 58, y + 43, name, false, 0);
            }

            _text.DrawWindow(3, 96, y + 57, partner.ShownExp.ToString(CultureInfo.InvariantCulture), true, 0);
            if (partner.Selected)
            {
                DrawFrame(0x24 + partner.CursorFrame % 2, 19, y + 43);
            }
        }

        private void DrawMessage(FightReportMessage? message)
        {
            if (message is null || _catalog is null)
            {
                return;
            }

            var substitutions = message.Substitutions.Select(BattleTextCatalog.Number).ToArray();
            var line = _catalog.Report(message.Entry == 2 ? "expGained" : message.Entry == 10 ? "moneyGained" : "partnerLevel", substitutions);
            _text.DrawLine(1, 0x14, 0xc2, line, false, 0, message.TokensRevealed);
            if (message.ArrowVisible && _arrow is { Length: > 0 })
            {
                DrawTexture(_arrow[message.ArrowFrame % _arrow.Length], new Vector2(0x123, 0xd0) + _arrowOffset);
            }
        }

        private void DrawScaledFrame(int frame, Vector2 position, Vector2 scale, Vector2 pivot)
        {
            if (scale.X <= 0 || scale.Y <= 0)
            {
                return;
            }

            DrawSetTransform(position + pivot, 0, scale);
            DrawFrame(frame, -pivot.X, -pivot.Y);
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }

        private void DrawFrame(int frame, float x, float y, int row = 0, Color? modulate = null)
        {
            if (!_frames.TryGetValue(frame, out var rows))
            {
                return;
            }

            var offset = _offsets.TryGetValue(frame, out var value) ? value : Vector2.Zero;
            DrawTexture(rows[Math.Clamp(row, 0, rows.Length - 1)], new Vector2(x, y) + offset, modulate ?? Colors.White);
        }
    }
}
