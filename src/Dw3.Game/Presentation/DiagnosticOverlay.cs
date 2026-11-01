using Godot;
using Dw3Production.Simulation;
using NumericsVector2 = System.Numerics.Vector2;

namespace Dw3Production.Presentation;

/// <summary>Read-only F3 geometry from the exact maps consumed by simulation.</summary>
public sealed partial class DiagnosticOverlay
{
    private const float HudIntervalSeconds = 0.25f;
    private readonly DebugGeometry _world;
    private readonly CanvasLayer _hud;
    private readonly Label _label;
    private GeometryCache? _cache;
    private float _hudAge = HudIntervalSeconds;

    public DiagnosticOverlay(Node2D root)
    {
        _world = new DebugGeometry { ZIndex = 100, Visible = false };
        root.AddChild(_world);
        _hud = new CanvasLayer { Layer = 100, Visible = false };
        _label = new Label { Position = new Vector2(12, 12) };
        _label.AddThemeColorOverride("font_color", new Color(0.92f, 0.96f, 1f));
        _label.AddThemeFontSizeOverride("font_size", 15);
        _label.AddThemeStyleboxOverride("normal", new StyleBoxFlat
        {
            BgColor = new Color(0.02f, 0.03f, 0.06f, 0.78f),
            ContentMarginLeft = 6,
            ContentMarginTop = 3,
            ContentMarginRight = 6,
            ContentMarginBottom = 3,
        });
        _hud.AddChild(_label);
        root.AddChild(_hud);
    }

    public int GeometryBuilds { get; private set; }
    public int HudRefreshes { get; private set; }
    public int DrawRequests { get; private set; }

    /// <summary>Optional diagnostic-mode marker prepended to the HUD line (e.g. the DEC-019
    /// DW3_START_ZONE selection). Null in a normal interactive session.</summary>
    public string? DiagnosticBadge { get; set; }

    public void Update(FieldZone zone, GameState state, bool visible, float actualFps, double deltaSeconds, Rect2 viewport)
    {
        _world.Visible = visible;
        _hud.Visible = visible;
        if (!visible) return;
        if (_cache is null || !ReferenceEquals(_cache.Zone, zone))
        {
            _cache = GeometryCache.Build(zone);
            GeometryBuilds++;
        }

        _hudAge += (float)Math.Max(0, deltaSeconds);
        if (_hudAge >= HudIntervalSeconds)
        {
            _hudAge = 0;
            _label.Text = $"{(DiagnosticBadge is { } badge ? badge + "   " : "")}{actualFps:0} FPS  {zone.Zone.Id}   green path  red block  amber unknown  cyan exit  orange gated exit  magenta climb";
            HudRefreshes++;
        }
        _world.Update(_cache, viewport, new Vector2(state.Position.X, state.Position.Y));
        DrawRequests++;
    }

    private sealed class GeometryCache
    {
        public required FieldZone Zone { get; init; }
        public required IReadOnlyList<Rect2> Walkable { get; init; }
        public required IReadOnlyList<Rect2> Blocked { get; init; }
        public required IReadOnlyList<Rect2> Unknown { get; init; }
        public required IReadOnlyList<Rect2> Occupants { get; init; }
        public required IReadOnlyList<Rect2> UnconditionalExits { get; init; }
        public required IReadOnlyList<Rect2> UnevaluatedExits { get; init; }
        public required IReadOnlyList<Rect2> Climbs { get; init; }

        public static GeometryCache Build(FieldZone zone)
        {
            var walkable = new List<Rect2>();
            var blocked = new List<Rect2>();
            var unknown = new List<Rect2>();
            var exitStatus = zone.Zone.Exits.ToDictionary(exit => exit.Slot, exit => exit.IsUnevaluated);
            var climbSlots = zone.Zone.Climbs.ToDictionary(climb => climb.Slot, climb => climb);
            var unconditionalExits = new List<Rect2>();
            var unevaluatedExits = new List<Rect2>();
            var climbs = new List<Rect2>();
            // This is the sole full-map pass, performed once per immutable FieldZone.
            // It uses the same source-backed floor admission as Locomotion: raw zero
            // rejects a ground probe, valid slope rows admit a walk step, and every
            // other value remains explicitly unknown rather than being painted safe.
            for (var y = 0; y < zone.Floor.Height; y++)
            {
                var runStart = 0;
                var runClass = Classify(zone.Floor.CellAt(new NumericsVector2(0, y)));
                for (var x = 1; x <= zone.Floor.Width; x++)
                {
                    var same = x < zone.Floor.Width && Classify(zone.Floor.CellAt(new NumericsVector2(x, y))) == runClass;
                    if (same) continue;
                    var rect = new Rect2(runStart, y, x - runStart, 1);
                    (runClass == FloorClass.Walkable ? walkable : runClass == FloorClass.Blocked ? blocked : unknown).Add(rect);
                    if (x < zone.Floor.Width)
                    {
                        runStart = x;
                        runClass = Classify(zone.Floor.CellAt(new NumericsVector2(x, y)));
                    }
                }
                for (var x = 0; x < zone.Triggers.Width; x++)
                {
                    var raw = zone.Triggers.CellAt(new NumericsVector2(x, y));
                    if (raw == 0) continue;
                    var slot = raw & 0x1F;
                    if (exitStatus.TryGetValue(slot, out var unevaluated))
                        (unevaluated ? unevaluatedExits : unconditionalExits).Add(new Rect2(x, y, 1, 1));
                    else if (climbSlots.ContainsKey(slot))
                        climbs.Add(new Rect2(x, y, 1, 1));
                }
            }
            var occupants = zone.Occupants.Select(actor => new Rect2(actor.Position.X - actor.Width / 2f, actor.Position.Y - actor.Width / 4f, actor.Width, actor.Width / 2f)).ToArray();
            return new GeometryCache { Zone = zone, Walkable = walkable, Blocked = blocked, Unknown = unknown, Occupants = occupants, UnconditionalExits = unconditionalExits, UnevaluatedExits = unevaluatedExits, Climbs = climbs };
        }

        private enum FloorClass { Walkable, Blocked, Unknown }

        private static FloorClass Classify(byte cell)
        {
            if (cell == 0) return FloorClass.Blocked;
            var row = cell & 0x0F;
            row -= row != 0 ? 1 : 0;
            return row is >= 0 and <= 6 ? FloorClass.Walkable : FloorClass.Unknown;
        }
    }

    private sealed partial class DebugGeometry : Node2D
    {
        private GeometryCache? _cache;
        private Rect2 _viewport;
        private Vector2 _playerPosition;

        public void Update(GeometryCache cache, Rect2 viewport, Vector2 playerPosition)
        {
            if (!ReferenceEquals(_cache, cache) || _viewport != viewport || _playerPosition != playerPosition)
            {
                _cache = cache;
                _viewport = viewport;
                _playerPosition = playerPosition;
                QueueRedraw();
            }
        }

        public override void _Draw()
        {
            if (_cache is null) return;
            DrawVisible(_cache.Walkable, new Color(0.15f, 1f, 0.25f, 0.20f), filled: true, 1);
            DrawVisible(_cache.Blocked, new Color(1f, 0.15f, 0.15f, 0.45f), filled: true, 1);
            DrawVisible(_cache.Unknown, new Color(1f, 0.65f, 0.1f, 0.55f), filled: true, 1);
            DrawVisible(_cache.Occupants, new Color(0.75f, 0.2f, 1f, 0.95f), filled: false, 0.75f);
            DrawVisible(_cache.UnconditionalExits, new Color(0.1f, 0.9f, 1f, 0.95f), filled: false, 1.5f);
            DrawVisible(_cache.UnevaluatedExits, new Color(1f, 0.5f, 0f, 0.95f), filled: false, 1.5f);
            DrawVisible(_cache.Climbs, new Color(1f, 0.2f, 0.9f, 0.95f), filled: false, 1.5f);
            DrawPlayerCollisionShape(_playerPosition);
        }

        private void DrawVisible(IReadOnlyList<Rect2> shapes, Color color, bool filled, float width)
        {
            foreach (var shape in shapes)
            {
                if (!shape.Intersects(_viewport)) continue;
                if (filled) DrawRect(shape, color);
                else DrawRect(shape, color, filled: false, width);
            }
        }

        private static readonly Color PlayerCollisionColor = new(1f, 1f, 0f);
        private static readonly Color SourceDiamondColor = new(1f, 1f, 1f, 0.7f);

        // Draws the active rounded collision shape (circle) at the actor position, plus
        // the removed source diamond octagon as a dashed reference. Presentation-only:
        // it reads the same radius the simulation uses and never feeds back into it.
        private void DrawPlayerCollisionShape(Vector2 position)
        {
            var radius = Locomotion.PlayerCollisionRadius;
            var bounds = new Rect2(position.X - radius, position.Y - radius, radius * 2f, radius * 2f);
            if (!bounds.Intersects(_viewport)) return;
            DrawArc(position, radius, 0f, Mathf.Tau, 64, PlayerCollisionColor, width: 2f, antialiased: true);
            var outline = Locomotion.SourceDiamondOutline;
            for (var i = 0; i < outline.Length; i++)
            {
                var next = (i + 1) % outline.Length;
                var from = position + new Vector2(outline[i].X, outline[i].Y);
                var to = position + new Vector2(outline[next].X, outline[next].Y);
                DrawDashedLine(from, to, SourceDiamondColor, width: 1f, dash: 2f);
            }
        }
    }
}
