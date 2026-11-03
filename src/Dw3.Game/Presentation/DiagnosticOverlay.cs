using Dw3Production.Conversion;
using Dw3Production.Simulation;
using Godot;
using NumericsVector2 = System.Numerics.Vector2;

namespace Dw3Production.Presentation;

/// <summary>Read-only F3 geometry from the exact maps consumed by simulation.</summary>
public sealed partial class DiagnosticOverlay : IDisposable
{
    private const float HudIntervalSeconds = 0.25f;

    private readonly DebugGeometry _world;

    private readonly CanvasLayer _hud;

    private readonly Label _label;

    private GeometryCache? _cache;

    private float _hudAge = HudIntervalSeconds;

    /// <summary>
    /// Initializes a new instance of the <see cref="DiagnosticOverlay"/> class.
    /// Creates a hidden DebugGeometry node for world-space geometry and a CanvasLayer HUD label,
    /// both added as children of <paramref name="root"/>.
    /// </summary>
    /// <param name="root">The Godot Node2D that receives the debug geometry and HUD canvas layer as children.</param>
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

    /// <summary>Gets the count of cached geometry builds for loaded field zones.</summary>
    public int GeometryBuilds { get; private set; }

    /// <summary>Gets the count of diagnostic HUD text refreshes.</summary>
    public int HudRefreshes { get; private set; }

    /// <summary>Gets the count of visible overlay draw updates.</summary>
    public int DrawRequests { get; private set; }

    /// <summary>Gets or sets optional diagnostic-mode marker prepended to the HUD line (e.g. the DEC-019
    /// DW3_START_ZONE selection). Null in a normal interactive session.</summary>
    public string? DiagnosticBadge { get; set; }

    /// <summary>Releases Godot resources owned by this overlay.</summary>
    public void Dispose()
    {
        _label.Dispose();
        _hud.Dispose();
        _world.Dispose();
    }

    /// <summary>Updates diagnostic geometry and HUD content for the current field frame.</summary>
    /// <param name="zone">Field zone supplying collision and trigger geometry.</param>
    /// <param name="state">Current field simulation state.</param>
    /// <param name="visible">Gets a value indicating whether diagnostics should be shown.</param>
    /// <param name="actualFps">Measured rendering frame rate.</param>
    /// <param name="deltaSeconds">Elapsed frame duration.</param>
    /// <param name="viewport">Visible world rectangle.</param>
    public void Update(FieldZone zone, GameState state, bool visible, float actualFps, double deltaSeconds, Rect2 viewport)
    {
        _world.Visible = visible;
        _hud.Visible = visible;
        if (!visible)
        {
            return;
        }

        if (_cache is null || !ReferenceEquals(_cache.Zone, zone))
        {
            _cache = GeometryCache.Build(zone);
            GeometryBuilds++;
        }

        _hudAge += (float)Math.Max(0, deltaSeconds);
        if (_hudAge >= HudIntervalSeconds)
        {
            _hudAge = 0;
            _label.Text = $"{(DiagnosticBadge is { } badge ? badge + "   " : string.Empty)}{actualFps:0} FPS  {zone.Zone.Id}   green path  red block  amber unknown  cyan exit  orange gated exit  magenta climb";
            HudRefreshes++;
        }

        _world.Update(_cache, viewport, new Vector2(state.Position.X, state.Position.Y));
        DrawRequests++;
    }

    /// <summary>Caches classified field geometry for a single immutable zone.</summary>
    private sealed class GeometryCache
    {
        /// <summary>Classifies a floor cell for diagnostic rendering.</summary>
        private enum FloorClass
        {
            /// <summary>Marks a cell that admits normal movement.</summary>
            Walkable,

            /// <summary>Marks a cell that blocks movement.</summary>
            Blocked,

            /// <summary>Marks a cell with an unclassified movement value.</summary>
            Unknown,
        }

        /// <summary>Gets the field zone represented by this cache.</summary>
        public required FieldZone Zone { get; init; }

        /// <summary>Gets merged walkable floor rectangles.</summary>
        public required IReadOnlyList<Rect2> Walkable { get; init; }

        /// <summary>Gets merged blocked floor rectangles.</summary>
        public required IReadOnlyList<Rect2> Blocked { get; init; }

        /// <summary>Gets floor rectangles with unclassified values.</summary>
        public required IReadOnlyList<Rect2> Unknown { get; init; }

        /// <summary>Gets collision rectangles for zone occupants.</summary>
        public required IReadOnlyList<Rect2> Occupants { get; init; }

        /// <summary>Gets trigger rectangles for unconditional exits.</summary>
        public required IReadOnlyList<Rect2> UnconditionalExits { get; init; }

        /// <summary>Gets trigger rectangles for exits pending evaluation.</summary>
        public required IReadOnlyList<Rect2> UnevaluatedExits { get; init; }

        /// <summary>Gets trigger rectangles for climbing transitions.</summary>
        public required IReadOnlyList<Rect2> Climbs { get; init; }

        /// <summary>Builds cached diagnostic geometry for one field zone.</summary>
        /// <param name="zone">Zone to classify.</param>
        /// <returns>Geometry cache for the zone.</returns>
        public static GeometryCache Build(FieldZone zone)
        {
            var exitStatus = zone.Zone.Exits.ToDictionary(exit => exit.Slot, exit => exit.IsUnevaluated);
            var climbSlots = zone.Zone.Climbs.ToDictionary(climb => climb.Slot, climb => climb);
            var walkable = new List<Rect2>();
            var blocked = new List<Rect2>();
            var unknown = new List<Rect2>();
            var unconditionalExits = new List<Rect2>();
            var unevaluatedExits = new List<Rect2>();
            var climbs = new List<Rect2>();

            // This is the sole full-map pass, performed once per immutable FieldZone.
            // It uses the same source-backed floor admission as Locomotion: raw zero
            // rejects a ground probe, valid slope rows admit a walk step, and every
            // other value remains explicitly unknown rather than being painted safe.
            for (var y = 0; y < zone.Floor.Height; y++)
            {
                ScanFloorRow(zone, y, walkable, blocked, unknown);
                ScanTriggerRow(zone, y, exitStatus, climbSlots, unconditionalExits, unevaluatedExits, climbs);
            }

            var occupants = zone.Occupants.Select(actor => new Rect2(actor.Position.X - (actor.Width / 2f), actor.Position.Y - (actor.Width / 4f), actor.Width, actor.Width / 2f)).ToArray();
            return new GeometryCache
            {
                Zone = zone,
                Walkable = walkable,
                Blocked = blocked,
                Unknown = unknown,
                Occupants = occupants,
                UnconditionalExits = unconditionalExits,
                UnevaluatedExits = unevaluatedExits,
                Climbs = climbs,
            };
        }

        /// <summary>Classifies contiguous floor cells from one row into merged rectangles.</summary>
        /// <param name="zone">Zone whose floor is scanned.</param>
        /// <param name="y">Floor row to scan.</param>
        /// <param name="walkable">Destination collection for walkable runs.</param>
        /// <param name="blocked">Destination collection for blocked runs.</param>
        /// <param name="unknown">Destination collection for unknown runs.</param>
        private static void ScanFloorRow(FieldZone zone, int y, List<Rect2> walkable, List<Rect2> blocked, List<Rect2> unknown)
        {
            var runStart = 0;
            var runClass = Classify(zone.Floor.CellAt(new NumericsVector2(0, y)));
            for (var x = 1; x <= zone.Floor.Width; x++)
            {
                var same = x < zone.Floor.Width && Classify(zone.Floor.CellAt(new NumericsVector2(x, y))) == runClass;
                if (same)
                {
                    continue;
                }

                var rect = new Rect2(runStart, y, x - runStart, 1);
                List<Rect2> runs = runClass switch
                {
                    FloorClass.Walkable => walkable,
                    FloorClass.Blocked => blocked,
                    _ => unknown,
                };
                runs.Add(rect);
                if (x < zone.Floor.Width)
                {
                    runStart = x;
                    runClass = Classify(zone.Floor.CellAt(new NumericsVector2(x, y)));
                }
            }
        }

        /// <summary>Classifies trigger cells from one row into diagnostic rectangles.</summary>
        /// <param name="zone">Zone whose triggers are scanned.</param>
        /// <param name="y">Trigger row to scan.</param>
        /// <param name="exitStatus">Exit slots indexed by evaluation status.</param>
        /// <param name="climbSlots">Climb slots indexed by identifier.</param>
        /// <param name="unconditionalExits">Destination collection for unconditional exits.</param>
        /// <param name="unevaluatedExits">Destination collection for pending exits.</param>
        /// <param name="climbs">Destination collection for climb triggers.</param>
        private static void ScanTriggerRow(FieldZone zone, int y, Dictionary<int, bool> exitStatus, Dictionary<int, Climb> climbSlots, List<Rect2> unconditionalExits, List<Rect2> unevaluatedExits, List<Rect2> climbs)
        {
            for (var x = 0; x < zone.Triggers.Width; x++)
            {
                var raw = zone.Triggers.CellAt(new NumericsVector2(x, y));
                if (raw == 0)
                {
                    continue;
                }

                var slot = raw & 0x1F;
                if (exitStatus.TryGetValue(slot, out var unevaluated))
                {
                    (unevaluated ? unevaluatedExits : unconditionalExits).Add(new Rect2(x, y, 1, 1));
                }
                else if (climbSlots.ContainsKey(slot))
                {
                    climbs.Add(new Rect2(x, y, 1, 1));
                }
            }
        }

        /// <summary>Maps one encoded floor cell to a diagnostic class.</summary>
        /// <param name="cell">Encoded floor-cell value.</param>
        /// <returns>The matching diagnostic class.</returns>
        private static FloorClass Classify(byte cell)
        {
            if (cell == 0)
            {
                return FloorClass.Blocked;
            }

            var row = cell & 0x0F;
            row -= row != 0 ? 1 : 0;
            return row is >= 0 and <= 6 ? FloorClass.Walkable : FloorClass.Unknown;
        }
    }

    /// <summary>Draws cached diagnostic shapes that intersect the current viewport.</summary>
    private sealed partial class DebugGeometry : Node2D
    {
        private static readonly Color PlayerCollisionColor = new(1f, 1f, 0f);

        private static readonly Color SourceDiamondColor = new(1f, 1f, 1f, 0.7f);

        private GeometryCache? _cache;

        private Rect2 _viewport;

        private Vector2 _playerPosition;

        /// <summary>Updates cached drawing inputs and requests redraw when they changed.</summary>
        /// <param name="cache">Geometry cache to draw.</param>
        /// <param name="viewport">Current visible world rectangle.</param>
        /// <param name="playerPosition">Current player position.</param>
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

        /// <summary>Draws visible collision, trigger, and player-shape diagnostics.</summary>
        public override void _Draw()
        {
            if (_cache is null)
            {
                return;
            }

            DrawVisible(_cache.Walkable, new Color(0.15f, 1f, 0.25f, 0.20f), filled: true, 1);
            DrawVisible(_cache.Blocked, new Color(1f, 0.15f, 0.15f, 0.45f), filled: true, 1);
            DrawVisible(_cache.Unknown, new Color(1f, 0.65f, 0.1f, 0.55f), filled: true, 1);
            DrawVisible(_cache.Occupants, new Color(0.75f, 0.2f, 1f, 0.95f), filled: false, 0.75f);
            DrawVisible(_cache.UnconditionalExits, new Color(0.1f, 0.9f, 1f, 0.95f), filled: false, 1.5f);
            DrawVisible(_cache.UnevaluatedExits, new Color(1f, 0.5f, 0f, 0.95f), filled: false, 1.5f);
            DrawVisible(_cache.Climbs, new Color(1f, 0.2f, 0.9f, 0.95f), filled: false, 1.5f);
            DrawPlayerCollisionShape(_playerPosition);
        }

        /// <summary>Draws shapes that intersect the current viewport.</summary>
        /// <param name="shapes">Shapes to consider.</param>
        /// <param name="color">Drawing colour.</param>
        /// <param name="filled">Gets a value indicating whether shapes are filled.</param>
        /// <param name="width">Outline width for unfilled shapes.</param>
        private void DrawVisible(IReadOnlyList<Rect2> shapes, Color color, bool filled, float width)
        {
            foreach (var shape in shapes)
            {
                if (!shape.Intersects(_viewport))
                {
                    continue;
                }

                if (filled)
                {
                    DrawRect(shape, color);
                }
                else
                {
                    DrawRect(shape, color, filled: false, width);
                }
            }
        }

        /// <summary>Draws the active player collision circle and reference outline without changing simulation state.</summary>
        /// <param name="position">Player position in world coordinates.</param>
        private void DrawPlayerCollisionShape(Vector2 position)
        {
            var radius = Locomotion.PlayerCollisionRadius;
            var bounds = new Rect2(position.X - radius, position.Y - radius, radius * 2f, radius * 2f);
            if (!bounds.Intersects(_viewport))
            {
                return;
            }

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
