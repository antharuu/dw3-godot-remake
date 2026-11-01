using Godot;
using Dw3Production.Conversion;
using Dw3Production.Simulation;

namespace Dw3Production.Presentation;

/// <summary>
/// Owns the Godot nodes for the active field scene: depth ordering, terrain, player and NPC sprites,
/// water and exit-cue animation, camera, connection icon, and the optional F3 diagnostic overlay.
/// It observes simulation state to present it but never decides movement, collisions, warps, or rules.
/// </summary>
public sealed class FieldRenderer
{
    private sealed class WaterRuntime
    {
        public required Node2D Pair;
        public required Sprite2D Opaque;
        public required Sprite2D Semi;
        public required WaterObject Object;
        public int Frame;
        public int Clut;
        public int CycleTime;
    }

    private sealed class NpcRuntime
    {
        public required Sprite2D Sprite;
        public required NpcRecord Record;
        public int FrameCursor;
        public int TicksRemaining;
    }

    private readonly Node2D _root;
    private readonly Dictionary<int, Node2D> _depthRoots = [];
    private readonly Camera2D _camera;
    private readonly CameraController _cameraController;
    private readonly DiagnosticOverlay _diagnosticOverlay;
    private Sprite2D? _player;
    private Sprite2D? _cuePop;
    private Sprite2D? _cueInner;
    private Sprite2D? _iconSprite;
    private readonly PlayerIconData _iconData;
    private readonly ExitCue _cue;
    private readonly WaterData _water;
    private readonly ExitCueState _cueState;
    private readonly List<WaterRuntime> _waterSprites = [];
    private readonly List<NpcRuntime> _npcSprites = [];
    private int _localSimulationSteps;
    private int _lastSourceTick;
    private readonly Dictionary<string, Texture2D> _textures = new(StringComparer.Ordinal);
    private readonly Dictionary<int, ShaderMaterial> _waterBlendMaterials = [];
    private ExitCueSnapshot _cueSnapshot = new(ExitCuePhase.Hidden, 0, null, 0);

    public int ImageDecodes { get; private set; }
    public int TextureUploads { get; private set; }
    public int PresentCalls { get; private set; }
    public int SimulationPresentationSteps { get; private set; }
    public int OverlayGeometryBuilds => _diagnosticOverlay.GeometryBuilds;
    public int OverlayHudRefreshes => _diagnosticOverlay.HudRefreshes;
    /// <summary>Gets or sets optional diagnostic-mode marker shown in the F3 HUD (DEC-019 DW3_START_ZONE).</summary>
    public string? DiagnosticBadge { get => _diagnosticOverlay.DiagnosticBadge; set => _diagnosticOverlay.DiagnosticBadge = value; }
    /// <summary>Gets number of visible NPC sprites currently in the loaded zone (test-only: re-entry leak check).</summary>
    public int NpcSpriteCount => _npcSprites.Count;
    /// <summary>
    /// Gets or sets a value indicating whether test-only: when true, skips the Reverse() step in ArrangeCallbacksAtPlayerGroundY
    /// so lower-SortY items draw last (wrong order) — used to confirm regression sensitivity.
    /// </summary>
    public bool TestReverseCallbackOrder { get; set; }
    private Zone? _zone;
    private PlayerData? _playerData;
    private ClimbData? _climbData;
    private Func<int, string>? _framePath;
    private Func<int, int, string>? _climbFramePath;
    private Sprite2D? _playerShadow;
    private Texture2D? _shadowTexture;

    public FieldRenderer(Node2D root, ExitCue cue, WaterData water, PlayerIconData icon)
    {
        _root = root;
        _iconData = icon;
        foreach (var depth in new[] { 11, 7, 4, 3, 2, 1 })
        {
            var depthRoot = new Node2D { ZIndex = SourceDepth.ToGodotZIndex(depth), ZAsRelative = false };
            _depthRoots.Add(depth, depthRoot);
            _root.AddChild(depthRoot);
        }
        _cue = cue;
        _water = water;
        _cueState = new ExitCueState(cue.InnerRows);
        _camera = new Camera2D();
        _root.AddChild(_camera);
        _cameraController = new CameraController(_camera);
        _diagnosticOverlay = new DiagnosticOverlay(_root);
    }

    /// <summary>
    /// Replaces field-scene render nodes for <paramref name="zone"/>. Queued children belong to the
    /// previous zone, while textures remain cached for reuse; <see cref="FieldZone"/> is the runtime
    /// aggregate used by simulation, whereas <see cref="Zone"/> is this data-pack render contract.
    /// </summary>
    public void LoadZone(Zone zone, PlayerData player, ClimbData climb, Func<int, string> framePath, Func<int, int, string> climbFramePath, IReadOnlyList<NpcRecord>? npcs)
    {
        foreach (var depthRoot in _depthRoots.Values)
            foreach (var child in depthRoot.GetChildren()) child.QueueFree();
        _zone = zone;
        _playerData = player;
        _climbData = climb;
        _framePath = framePath;
        _climbFramePath = climbFramePath;
        foreach (var layer in zone.Layers)
        {
            var sprite = new Sprite2D { Texture = TextureFor(layer.Path), Centered = false };
            // Apply the source stream layer modulate to the depth-11 (background) sprite.
            // In the field-layer record contract, FIELDSTG_spriteDepths[2]=11 always uses FIELDSTG_state.spriteColor
            // (i==2 condition), giving each stage its ambient tint.
            // S200: stageColor=(0x54,0x67,0x96) makes the grey canal tiles appear blue.
            if (layer.SourceDepth == 11 && zone.Depth11Modulate is { } mod)
                sprite.Modulate = new Color(mod.R, mod.G, mod.B);
            DepthRoot(layer.SourceDepth).AddChild(sprite);
        }
        _waterSprites.Clear();
        _localSimulationSteps = 0;
        _lastSourceTick = 0;
        if (_water.Zones.TryGetValue(zone.Id, out var zoneWater))
        {
            foreach (var water in zoneWater)
            {
                // Keep opaque and STP passes under one sibling: source draws both parts
                // atomically at the object's callback/immediate slot.
                var pair = new Node2D();
                var opaque = new Sprite2D { Centered = false };
                var semi = new Sprite2D { Centered = false, Material = WaterBlendMaterial(-1) };
                pair.AddChild(opaque);
                pair.AddChild(semi);
                DepthRoot(water.Depth).AddChild(pair);
                var runtime = new WaterRuntime { Pair = pair, Opaque = opaque, Semi = semi, Object = water, Frame = water.Frame, Clut = water.ClutRow, CycleTime = water.CycleTime };
                ApplyWaterFrame(runtime);
                _waterSprites.Add(runtime);
            }
        }
        _npcSprites.Clear();
        if (npcs is not null)
        {
            foreach (var npc in npcs)
            {
                var sprite = new Sprite2D { Centered = false };
                DepthRoot(4).AddChild(sprite);
                var runtime = new NpcRuntime { Sprite = sprite, Record = npc, FrameCursor = 0, TicksRemaining = npc.IdleFrames[0].Duration };
                ApplyNpcFrame(runtime);
                _npcSprites.Add(runtime);
            }
        }
        _playerShadow = new Sprite2D { Centered = false, Visible = false, Texture = ShadowTexture() };
        DepthRoot(4).AddChild(_playerShadow);
        _player = new Sprite2D { Centered = false };
        DepthRoot(4).AddChild(_player);
        _iconSprite = new Sprite2D { Centered = false, Visible = false };
        DepthRoot(2).AddChild(_iconSprite);
        _cuePop = new Sprite2D { Centered = false, Visible = false };
        _cueInner = new Sprite2D { Centered = false, Visible = false };
        DepthRoot(1).AddChild(_cuePop);
        DepthRoot(1).AddChild(_cueInner);
        _cueState.Reset();
        _cueSnapshot = new ExitCueSnapshot(ExitCuePhase.Hidden, 0, null, 0);
    }

    public ExitCueSnapshot CueSnapshot => _cueSnapshot;
    public CueFrame? CurrentCuePopFrame => _cueSnapshot.Visible ? _cue.Frames[_cueSnapshot.PopFrame] : null;

    public void AdvanceSimulation(GameState state, FieldZone zone)
    {
        SimulationPresentationSteps++;
        _localSimulationSteps++;
        var sourceTick = (_localSimulationSteps * 50) / 60;
        while (_lastSourceTick < sourceTick)
        {
            foreach (var water in _waterSprites)
                if (IsSourceVisible(water)) AdvanceWaterObject(water);
            foreach (var npc in _npcSprites)
                AdvanceNpc(npc);
            _lastSourceTick++;
        }
        var rawDirection = state.Offer && state.OfferedTriggerDirection >= 0
            ? state.OfferedTriggerDirection
            : zone.Triggers.CellAt(state.Position) >> 5;
        _cueSnapshot = _cueState.Advance(state.Offer, rawDirection);
    }

    public void Present(GameState state, FieldZone zone, bool f3On, double deltaSeconds = 0, IconView? iconView = null)
    {
        if (_zone is null || _player is null || _playerData is null || _framePath is null) throw new InvalidOperationException("renderer has no loaded zone");
        PresentCalls++;
        // Climbing swaps the frame source to the climb archive and lifts the sprite by
        // climbHeight/256 pixels while the shadow stays at the foot (source drawActor:
        // tile.y = (pos.y - climbHeight) >> 8, shadow at pos.y/256 - 8).
        var climbing = state.ClimbActive && _climbData is not null && _climbFramePath is not null;
        if (climbing)
        {
            var climbSample = ClimbAnimation.Sample(_climbData!, state.ClimbAnimationSet, state.ClimbAnimationTicks);
            _player.Texture = TextureFor(_climbFramePath!(state.ClimbAnimationSet, climbSample.Frame));
            // The source mirrors the frame offset for dir >= 5 (drawActor: x = -(frameWidth + x)).
            // The climb frames carry an asymmetric anchor (-21), unlike the player's symmetric -16,
            // so the raw offset must be mirrored here or the sprite lands 10 px too far left.
            var climbFlipH = state.AnimationDirection >= 5;
            var climbOffsetX = climbFlipH ? -(_player.Texture.GetWidth() + climbSample.OffsetX) : climbSample.OffsetX;
            _player.Offset = new Vector2(climbOffsetX, climbSample.OffsetY);
            _player.FlipH = climbFlipH;
            _player.Position = new Vector2(state.Position.X, state.Position.Y - state.ClimbHeight / 256f);
            if (_playerShadow is not null)
            {
                _playerShadow.Visible = true;
                _playerShadow.Position = new Vector2(state.Position.X - 16, state.Position.Y - 8);
            }
        }
        else
        {
            var sample = PlayerAnimation.Sample(_playerData, state);
            _player.Texture = TextureFor(_framePath(sample.Frame));
            _player.Offset = new Vector2(sample.OffsetX, sample.OffsetY);
            _player.FlipH = sample.FlipH;
            _player.Position = new Vector2(state.Position.X, state.Position.Y);
            _playerShadow?.Visible = false;
        }
        PresentIcon(iconView);
        var sortY = Mathf.FloorToInt(state.Position.Y - (climbing ? state.ClimbHeight / 256f : 0f));
        ArrangeCallbacksAtPlayerGroundY(sortY);
        var cameraSmooth = climbing || state.CameraSmoothingTicks > 0;
        if (state.CameraSmoothingTicks > 0) state.CameraSmoothingTicks--;
        _cameraController.Apply(_zone, _player.Position, cameraSmooth);
        PresentCue(state);
        var visibleSize = GetViewportWorldSize();
        var viewport = new Rect2(_camera.Position - visibleSize / 2f, visibleSize);
        _diagnosticOverlay.Update(zone, state, f3On, (float)Engine.GetFramesPerSecond(), (float)deltaSeconds, viewport);
    }

    private Texture2D ShadowTexture()
    {
        // The source shadow is a 32x16 sprite drawn under the actor (drawActor sprts:
        // w=0x20, h=0x10 at pos.x-16, pos.y+climbHeight-8). The full field-image shadow
        // texture is not yet decoded, so a soft dark ellipse of the same footprint stands
        // in and stays at the actor's foot during a climb. Presentation-only.
        if (_shadowTexture is not null) return _shadowTexture;
        var image = Image.CreateEmpty(32, 16, false, Image.Format.Rgba8);
        for (var y = 0; y < 16; y++)
            for (var x = 0; x < 32; x++)
            {
                var dx = (x - 15.5f) / 16f;
                var dy = (y - 7.5f) / 8f;
                var distance = dx * dx + dy * dy;
                var alpha = distance >= 1f ? 0f : 0.35f * (1f - distance);
                image.SetPixel(x, y, new Color(0f, 0f, 0f, alpha));
            }
        _shadowTexture = ImageTexture.CreateFromImage(image);
        return _shadowTexture;
    }

    private Node2D DepthRoot(int depth)
    {
        if (_depthRoots.TryGetValue(depth, out var root)) return root;
        root = new Node2D { ZIndex = SourceDepth.ToGodotZIndex(depth), ZAsRelative = false };
        _depthRoots.Add(depth, root);
        _root.AddChild(root);
        return root;
    }

    private void ArrangeCallbacksAtPlayerGroundY(int playerGroundY)
    {
        if (_player is null) return;
        var depths = _waterSprites.Select(water => water.Object.Depth).ToHashSet();
        // Depth 4 always carries the player and every NPC, so sort it even when a zone
        // has no depth-4 map object (the actor and object callback lists are independent).
        depths.Add(4);
        foreach (var depth in depths)
        {
            var depthRoot = DepthRoot(depth);
            var callbacks = _waterSprites
                .Where(water => water.Object.Depth == depth && water.Object.SortY != 0)
                .Select(water => (Item: (Node)water.Pair, Priority: water.Object.SortY, Registration: SourceCallbackOrder.MapObjectRegistration))
                .ToList();
            if (depth == 4)
            {
                callbacks.Add((_player, playerGroundY, SourceCallbackOrder.PlayerRegistration));
                foreach (var npc in _npcSprites)
                    callbacks.Add(((Node)npc.Sprite, npc.Record.Y, SourceCallbackOrder.MapObjectRegistration));
            }
            var arranged = SourceCallbackOrder.Order(callbacks);
            IEnumerable<Node> toProcess = TestReverseCallbackOrder ? arranged : arranged.Reverse();
            foreach (var item in toProcess) depthRoot.MoveChild(item, 0);

            // Immediate map-object primitives are made before callbacks. Callback
            // primitives prepend their OT entry, so immediate items remain after
            // all callbacks in final painter order at this source depth.
            foreach (var water in _waterSprites.Where(water => water.Object.Depth == depth && water.Object.SortY == 0).OrderByDescending(water => water.Object.Index))
                depthRoot.MoveChild(water.Pair, -1);
        }
    }

    private bool IsSourceVisible(WaterRuntime water)
    {
        var size = GetViewportWorldSize();
        var view = new Rect2(_camera.Position - size / 2f, size);
        // the map-object visibility contract tests the immutable object origin, not its frame offset,
        // with the decoded one-sided margin on the left/top only.
        var origin = new Vector2(water.Object.X, water.Object.Y);
        return origin.X >= view.Position.X - water.Object.Margin
            && origin.X <= view.End.X
            && origin.Y >= view.Position.Y - water.Object.Margin
            && origin.Y <= view.End.Y;
    }

    private void AdvanceWaterObject(WaterRuntime water)
    {
        var obj = water.Object;
        if (obj.Cycle == 0) return;
        if (obj.Cycle == 1)
        {
            if (water.CycleTime < 0x100)
            {
                water.Frame = water.Frame >= obj.CycleLast ? obj.CycleFirst : water.Frame + 1;
                water.CycleTime = obj.CycleDelay << 8;
            }
            else water.CycleTime -= 0x100;
        }
        else if (obj.Cycle == 2)
        {
            if (water.CycleTime < 0x100)
            {
                water.Clut = water.Clut >= obj.CycleLast ? obj.CycleFirst : water.Clut + 1;
                water.CycleTime = obj.CycleDelay << 8;
            }
            else water.CycleTime -= 0x100;
        }
        else if (obj.Cycle == 3)
        {
            var back = (water.CycleTime & 0x8000) != 0;
            var time = water.CycleTime & 0x7fff;
            if (time < 0x100)
            {
                water.Clut += back ? -1 : 1;
                time = obj.CycleDelay << 8;
                if (back && water.Clut == obj.CycleFirst - 1) { water.Clut = obj.CycleFirst + 1; back = false; }
                else if (!back && water.Clut == obj.CycleLast + 1) { water.Clut = obj.CycleLast - 1; back = true; }
            }
            else time -= 0x100;
            water.CycleTime = time | (back ? 0x8000 : 0);
        }
        ApplyWaterFrame(water);
    }

    private void ApplyWaterFrame(WaterRuntime water)
    {
        var frame = water.Object.Frames.FirstOrDefault(candidate => FrameMatches(water.Object, candidate, water.Frame, water.Clut)) ?? water.Object.Frames[0];
        water.Opaque.Texture = TextureFor(frame.OpaquePath);
        water.Semi.Texture = TextureFor(frame.SemiPath);
        water.Semi.Material = WaterBlendMaterial(frame.Abr);
        var position = new Vector2(water.Object.X + frame.OffsetX, water.Object.Y + frame.OffsetY);
        water.Opaque.Position = position;
        water.Semi.Position = position;
    }

    private static bool FrameMatches(WaterObject obj, WaterFrame candidate, int frame, int clut)
    {
        // Converter orders cycle-1 frames by frame ID and cycles 2/3 by CLUT row.
        // The full source values stay in the manifest but paths are opaque, so map
        // the live source value onto the deterministic exported order.
        var index = obj.Cycle == 1 ? frame - obj.CycleFirst : obj.Cycle is 2 or 3 ? clut - obj.CycleFirst : 0;
        return index >= 0 && index < obj.Frames.Count && ReferenceEquals(candidate, obj.Frames[index]);
    }

    private void ApplyNpcFrame(NpcRuntime npc)
    {
        var frame = npc.Record.IdleFrames[npc.FrameCursor];
        npc.Sprite.Texture = TextureFor(frame.Path);
        npc.Sprite.Offset = new Vector2(frame.OffsetX, frame.OffsetY);
        npc.Sprite.FlipH = npc.Record.FlipH;
        npc.Sprite.Position = new Vector2(npc.Record.X, npc.Record.Y);
    }

    private void AdvanceNpc(NpcRuntime npc)
    {
        var frames = npc.Record.IdleFrames;
        if (frames.Count <= 1) return; // a single source frame is a static pose, never cycled
        if (--npc.TicksRemaining > 0) return;
        npc.FrameCursor = (npc.FrameCursor + 1) % frames.Count;
        npc.TicksRemaining = frames[npc.FrameCursor].Duration;
        ApplyNpcFrame(npc);
    }

    private ShaderMaterial WaterBlendMaterial(int abr)
    {
        if (_waterBlendMaterials.TryGetValue(abr, out var material)) return material;
        var code = abr switch
        {
            0 => "shader_type canvas_item; void fragment(){ COLOR = texture(TEXTURE, UV); COLOR.a *= 0.5; }",
            1 => "shader_type canvas_item; render_mode blend_add; void fragment(){ COLOR = texture(TEXTURE, UV); }",
            2 => "shader_type canvas_item; render_mode blend_sub; void fragment(){ COLOR = texture(TEXTURE, UV); }",
            3 => "shader_type canvas_item; render_mode blend_add; void fragment(){ COLOR = texture(TEXTURE, UV); COLOR.a *= 0.25; }",
            _ => "shader_type canvas_item; void fragment(){ COLOR = texture(TEXTURE, UV); }"
        };
        material = new ShaderMaterial { Shader = new Shader { Code = code } };
        _waterBlendMaterials.Add(abr, material);
        return material;
    }

    private Vector2 GetViewportWorldSize() => _root.GetViewport().GetVisibleRect().Size / 3f;

    private void PresentCue(GameState state)
    {
        if (_cuePop is null || _cueInner is null) throw new InvalidOperationException("cue sprites unavailable");
        if (!_cueSnapshot.Visible) { _cuePop.Visible = false; _cueInner.Visible = false; return; }
        ApplyCueSprite(_cuePop, _cue.Frames[_cueSnapshot.PopFrame], state);
        if (_cueSnapshot.InnerFrame is int innerFrame) ApplyCueSprite(_cueInner, _cue.Frames[innerFrame], state); else _cueInner.Visible = false;
    }

    private void ApplyCueSprite(Sprite2D sprite, CueFrame frame, GameState state)
    {
        sprite.Texture = TextureFor(frame.Path);
        sprite.Position = new Vector2(state.Position.X + frame.OffsetX, state.Position.Y - 27 + frame.OffsetY);
        sprite.Visible = true;
    }

    private void PresentIcon(IconView? view)
    {
        if (_iconSprite is null) return;
        if (view is not { } iconView)
        {
            _iconSprite.Visible = false;
            return;
        }
        var frame = _iconData.Frames[iconView.Frame];
        _iconSprite.Texture = TextureFor(frame.Path);
        _iconSprite.Offset = new Vector2(frame.OffsetX, frame.OffsetY);
        _iconSprite.Position = new Vector2(iconView.Position.X, iconView.Position.Y);
        _iconSprite.Visible = true;
    }

    private Texture2D TextureFor(string path)
    {
        if (_textures.TryGetValue(path, out var texture)) return texture;
        var image = Image.LoadFromFile(path);
        if (image is null || image.IsEmpty()) throw new CacheResourceException("CACHE_RESOURCE_UNAVAILABLE", $"image unreadable: {path}");
        ImageDecodes++;
        texture = ImageTexture.CreateFromImage(image);
        TextureUploads++;
        _textures.Add(path, texture);
        return texture;
    }
}
