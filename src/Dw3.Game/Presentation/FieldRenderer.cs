using Dw3Production.Conversion;
using Dw3Production.Simulation;
using Godot;

namespace Dw3Production.Presentation;

/// <summary>
/// Owns the Godot nodes for the active field scene: depth ordering, terrain, player and NPC sprites,
/// water and exit-cue animation, camera, connection icon, and the optional F3 diagnostic overlay.
/// It observes simulation state to present it but never decides movement, collisions, warps, or rules.
/// </summary>
public sealed class FieldRenderer : IDisposable
{
    private readonly Node2D _root;

    private readonly Dictionary<int, Node2D> _depthRoots = [];

    private readonly Camera2D _camera;

    private readonly CameraController _cameraController;

    private readonly DiagnosticOverlay _diagnosticOverlay;

    private readonly FieldTextureCache _textureCache;

    private readonly FieldWaterLayer _waterLayer;

    private readonly FieldNpcLayer _npcLayer;

    private readonly WaterData _water;

    private readonly PlayerIconData _iconData;

    private readonly ExitCue _cue;

    private readonly ExitCueState _cueState;

    private Sprite2D? _player;

    private Sprite2D? _cuePop;

    private Sprite2D? _cueInner;

    private Sprite2D? _iconSprite;

    private int _localSimulationSteps;

    private int _lastSourceTick;

    private ExitCueSnapshot _cueSnapshot = new(ExitCuePhase.Hidden, 0, null, 0);

    private Sprite2D? _playerShadow;

    private Zone? _zone;

    private PlayerData? _playerData;

    private ClimbData? _climbData;

    private Func<int, string>? _framePath;

    private Func<int, int, string>? _climbFramePath;

    /// <summary>
    /// Initializes a new instance of the <see cref="FieldRenderer"/> class.
    /// Creates depth-ordered sub-nodes (11, 7, 4, 3, 2, 1), a camera, a diagnostic overlay,
    /// a water layer, and an NPC layer, all rooted under <paramref name="root"/>.
    /// </summary>
    /// <param name="root">Parent Godot node that receives all depth-ordered sub-nodes, the camera, and the diagnostic overlay.</param>
    /// <param name="cue">Decoded exit-cue frames and per-direction animation sequences for the field connection balloon.</param>
    /// <param name="water">Decoded water domain data providing animated stage objects for each loaded zone.</param>
    /// <param name="icon">Decoded above-head connection indicator frames and animation sequences.</param>
    public FieldRenderer(Node2D root, ExitCue cue, WaterData water, PlayerIconData icon)
    {
        _root = root;
        _iconData = icon;
        _water = water;
        foreach (var depth in new[] { 11, 7, 4, 3, 2, 1 })
        {
            var depthRoot = new Node2D { ZIndex = SourceDepth.ToGodotZIndex(depth), ZAsRelative = false };
            _depthRoots.Add(depth, depthRoot);
            _root.AddChild(depthRoot);
        }

        _cue = cue;
        _cueState = new ExitCueState(cue.InnerRows);
        _camera = new Camera2D();
        _root.AddChild(_camera);
        _cameraController = new CameraController(_camera);
        _diagnosticOverlay = new DiagnosticOverlay(_root);
        _textureCache = new FieldTextureCache();
        _waterLayer = new FieldWaterLayer(_textureCache);
        _npcLayer = new FieldNpcLayer(_textureCache);
    }

    /// <summary>Gets image decode operations performed by the texture cache.</summary>
    public int ImageDecodes => _textureCache.ImageDecodes;

    /// <summary>Gets texture uploads performed by the texture cache.</summary>
    public int TextureUploads => _textureCache.TextureUploads;

    /// <summary>Gets the count of calls that present field state.</summary>
    public int PresentCalls { get; private set; }

    /// <summary>Gets the count of presentation-side simulation advancement calls.</summary>
    public int SimulationPresentationSteps { get; private set; }

    /// <summary>Gets geometry cache builds requested by the diagnostic overlay.</summary>
    public int OverlayGeometryBuilds => _diagnosticOverlay.GeometryBuilds;

    /// <summary>Gets diagnostic HUD text refreshes.</summary>
    public int OverlayHudRefreshes => _diagnosticOverlay.HudRefreshes;

    /// <summary>Gets or sets optional diagnostic-mode marker shown in the F3 HUD (DEC-019 DW3_START_ZONE).</summary>
    public string? DiagnosticBadge { get => _diagnosticOverlay.DiagnosticBadge; set => _diagnosticOverlay.DiagnosticBadge = value; }

    /// <summary>Gets number of visible NPC sprites currently in the loaded zone (test-only: re-entry leak check).</summary>
    public int NpcSpriteCount => _npcLayer.Count;

    /// <summary>
    /// Gets or sets a value indicating whether test-only: when true, skips the Reverse() step in ArrangeCallbacksAtPlayerGroundY
    /// so lower-SortY items draw last (wrong order) — used to confirm regression sensitivity.
    /// </summary>
    public bool TestReverseCallbackOrder { get; set; }

    /// <summary>Gets the current state of the field exit cue.</summary>
    public ExitCueSnapshot CueSnapshot => _cueSnapshot;

    /// <summary>Gets the current outer exit-cue frame when the cue is visible.</summary>
    public CueFrame? CurrentCuePopFrame => _cueSnapshot.Visible ? _cue.Frames[_cueSnapshot.PopFrame] : null;

    /// <summary>Releases Godot resources owned by this renderer.</summary>
    public void Dispose()
    {
        _waterLayer.Dispose();
        _textureCache.Dispose();
        _diagnosticOverlay.Dispose();
    }

    /// <summary>
    /// Replaces field-scene render nodes for <paramref name="zone"/>. Queued children belong to the
    /// previous zone, while textures remain cached for reuse; <see cref="FieldZone"/> is the runtime
    /// aggregate used by simulation, whereas <see cref="Zone"/> is this data-pack render contract.
    /// </summary>
    /// <summary>Replaces render nodes and data bindings for a loaded field zone.</summary>
    /// <param name="zone">Decoded zone render data.</param>
    /// <param name="player">Decoded walking animation data.</param>
    /// <param name="climb">Decoded climbing and drop animation data.</param>
    /// <param name="framePath">Resolves a walking frame identifier to a texture path.</param>
    /// <param name="climbFramePath">Resolves an action frame identifier to a texture path.</param>
    /// <param name="npcs">NPC records visible in the zone.</param>
    public void LoadZone(Zone zone, PlayerData player, ClimbData climb, Func<int, string> framePath, Func<int, int, string> climbFramePath, IReadOnlyList<NpcRecord>? npcs)
    {
        foreach (var depthRoot in _depthRoots.Values)
        {
            foreach (var child in depthRoot.GetChildren())
            {
                child.QueueFree();
            }
        }

        _zone = zone;
        _playerData = player;
        _climbData = climb;
        _framePath = framePath;
        _climbFramePath = climbFramePath;

        LoadZoneLayers(zone);
        _waterLayer.LoadZone(_water, zone, DepthRoot);
        _npcLayer.LoadZone(npcs, DepthRoot(4));

        _playerShadow = new Sprite2D { Centered = false, Visible = false, Texture = _textureCache.ShadowTexture() };
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
        _localSimulationSteps = 0;
        _lastSourceTick = 0;
    }

    /// <summary>Advances presentation-only water, NPC, and exit-cue state from simulation state.</summary>
    /// <param name="state">Current field simulation state.</param>
    /// <param name="zone">Current simulation field zone.</param>
    public void AdvanceSimulation(GameState state, FieldZone zone)
    {
        SimulationPresentationSteps++;
        _localSimulationSteps++;
        var sourceTick = (_localSimulationSteps * 50) / 60;
        var viewport = GetViewportRect();
        while (_lastSourceTick < sourceTick)
        {
            _waterLayer.Advance(viewport);
            _npcLayer.Advance();
            _lastSourceTick++;
        }

        var rawDirection = state.Offer && state.OfferedTriggerDirection >= 0
            ? state.OfferedTriggerDirection
            : zone.Triggers.CellAt(state.Position) >> 5;
        _cueSnapshot = _cueState.Advance(state.Offer, rawDirection);
    }

    /// <summary>Applies current field state to sprites, camera, cue, icon, and diagnostic overlay.</summary>
    /// <param name="state">Current field simulation state.</param>
    /// <param name="zone">Current simulation field zone.</param>
    /// <param name="f3On">Gets a value indicating whether diagnostic geometry is visible.</param>
    /// <param name="deltaSeconds">Elapsed frame duration used by diagnostics.</param>
    /// <param name="iconView">Optional connection-icon state.</param>
    public void Present(GameState state, FieldZone zone, bool f3On, double deltaSeconds = 0, IconView? iconView = null)
    {
        if (_zone is null || _player is null || _playerData is null || _framePath is null)
        {
            throw new InvalidOperationException("renderer has no loaded zone");
        }

        PresentCalls++;

        // Climbing swaps the frame source to the climb archive and lifts the sprite by
        // climbHeight/256 pixels while the shadow stays at the foot (source drawActor:
        // tile.y = (pos.y - climbHeight) >> 8, shadow at pos.y/256 - 8).
        var climbing = state.ClimbActive && _climbData is not null && _climbFramePath is not null;
        PresentPlayer(state, climbing);
        PresentIcon(iconView);
        var sortY = Mathf.FloorToInt(state.Position.Y - (climbing ? state.ClimbHeight / 256f : 0f));
        ArrangeCallbacksAtPlayerGroundY(sortY);
        var cameraSmooth = climbing || state.CameraSmoothingTicks > 0;
        if (state.CameraSmoothingTicks > 0)
        {
            state.CameraSmoothingTicks--;
        }

        _cameraController.Apply(_zone, _player.Position, cameraSmooth);
        PresentCue(state);
        var visibleSize = GetViewportWorldSize();
        var viewport = new Rect2(_camera.Position - (visibleSize / 2f), visibleSize);
        _diagnosticOverlay.Update(zone, state, f3On, (float)Engine.GetFramesPerSecond(), (float)deltaSeconds, viewport);
    }

    /// <summary>Creates terrain sprites for every layer in a zone.</summary>
    /// <param name="zone">Zone whose layers are rendered.</param>
    private void LoadZoneLayers(Zone zone)
    {
        foreach (var layer in zone.Layers)
        {
            var sprite = new Sprite2D { Texture = _textureCache.TextureFor(layer.Path), Centered = false };

            // Apply the source stream layer modulate to the depth-11 (background) sprite.
            // In the field-layer record contract, FIELDSTG_spriteDepths[2]=11 always uses FIELDSTG_state.spriteColor
            // (i==2 condition), giving each stage its ambient tint.
            // S200: stageColor=(0x54,0x67,0x96) makes the grey canal tiles appear blue.
            if (layer.SourceDepth == 11 && zone.Depth11Modulate is { } mod)
            {
                sprite.Modulate = new Color(mod.R, mod.G, mod.B);
            }

            DepthRoot(layer.SourceDepth).AddChild(sprite);
        }
    }

    /// <summary>Chooses the walking or climbing presentation for the player sprite.</summary>
    /// <param name="state">Current field simulation state.</param>
    /// <param name="climbing">Gets a value indicating whether the player uses action-animation frames.</param>
    private void PresentPlayer(GameState state, bool climbing)
    {
        if (climbing)
        {
            PresentClimbing(state);
        }
        else
        {
            PresentWalking(state);
        }
    }

    /// <summary>Updates sprite texture, anchor, and shadow for climbing movement.</summary>
    /// <param name="state">Current field simulation state.</param>
    private void PresentClimbing(GameState state)
    {
        if (_climbData is not { } climbData || _climbFramePath is not { } framePath || _player is not { } player)
        {
            return;
        }

        var climbSample = ClimbAnimation.Sample(climbData, state.ClimbAnimationSet, state.ClimbAnimationTicks);
        player.Texture = _textureCache.TextureFor(framePath(state.ClimbAnimationSet, climbSample.Frame));

        // The source mirrors the frame offset for dir >= 5 (drawActor: x = -(frameWidth + x)).
        // The climb frames carry an asymmetric anchor (-21), unlike the player's symmetric -16,
        // so the raw offset must be mirrored here or the sprite lands 10 px too far left.
        var climbFlipH = state.AnimationDirection >= 5;
        var climbOffsetX = climbFlipH ? -(player.Texture.GetWidth() + climbSample.OffsetX) : climbSample.OffsetX;
        player.Offset = new Vector2(climbOffsetX, climbSample.OffsetY);
        player.FlipH = climbFlipH;
        player.Position = new Vector2(state.Position.X, state.Position.Y - (state.ClimbHeight / 256f));
        if (_playerShadow is not null)
        {
            _playerShadow.Visible = true;
            _playerShadow.Position = new Vector2(state.Position.X - 16, state.Position.Y - 8);
        }
    }

    /// <summary>Updates sprite texture and anchor for ordinary walking movement.</summary>
    /// <param name="state">Current field simulation state.</param>
    private void PresentWalking(GameState state)
    {
        if (_playerData is not { } playerData || _framePath is not { } framePath || _player is not { } player)
        {
            return;
        }

        var sample = PlayerAnimation.Sample(playerData, state);
        player.Texture = _textureCache.TextureFor(framePath(sample.Frame));
        player.Offset = new Vector2(sample.OffsetX, sample.OffsetY);
        player.FlipH = sample.FlipH;
        player.Position = new Vector2(state.Position.X, state.Position.Y);
        if (_playerShadow is { } shadow)
        {
            shadow.Visible = false;
        }
    }

    /// <summary>Returns the render root for a source depth, creating it when required.</summary>
    /// <param name="depth">Source depth represented by the root.</param>
    /// <returns>The node that owns sprites at the requested depth.</returns>
    private Node2D DepthRoot(int depth)
    {
        if (_depthRoots.TryGetValue(depth, out var root))
        {
            return root;
        }

        root = new Node2D { ZIndex = SourceDepth.ToGodotZIndex(depth), ZAsRelative = false };
        _depthRoots.Add(depth, root);
        _root.AddChild(root);
        return root;
    }

    /// <summary>Orders callback sprites relative to the player ground position.</summary>
    /// <param name="playerGroundY">Player foot position used for depth sorting.</param>
    private void ArrangeCallbacksAtPlayerGroundY(int playerGroundY)
    {
        if (_player is null)
        {
            return;
        }

        var depths = _waterLayer.ActiveDepths().ToHashSet();

        // Depth 4 always carries the player and every NPC, so sort it even when a zone
        // has no depth-4 map object (the actor and object callback lists are independent).
        depths.Add(4);
        foreach (var depth in depths)
        {
            SortDepthCallbacks(depth, playerGroundY);
        }
    }

    /// <summary>Applies painter ordering to callbacks and immediate objects at one depth.</summary>
    /// <param name="depth">Source depth to order.</param>
    /// <param name="playerGroundY">Player foot position used for callback ordering.</param>
    private void SortDepthCallbacks(int depth, int playerGroundY)
    {
        var depthRoot = DepthRoot(depth);
        var callbacks = _waterLayer.CallbacksAtDepth(depth).ToList();
        if (depth == 4)
        {
            callbacks.Add((_player!, playerGroundY, SourceCallbackOrder.PlayerRegistration));
            foreach (var (sprite, y) in _npcLayer.Callbacks())
            {
                callbacks.Add((sprite, y, SourceCallbackOrder.MapObjectRegistration));
            }
        }

        var arranged = SourceCallbackOrder.Order(callbacks);
        IEnumerable<Node> toProcess = TestReverseCallbackOrder ? arranged : arranged.Reverse();
        foreach (var item in toProcess)
        {
            depthRoot.MoveChild(item, 0);
        }

        // Immediate map-object primitives are made before callbacks. Callback
        // primitives prepend their OT entry, so immediate items remain after
        // all callbacks in final painter order at this source depth.
        foreach (var obj in _waterLayer.ImmediateObjectsAtDepth(depth))
        {
            depthRoot.MoveChild(_waterLayer.PairForObject(obj), -1);
        }
    }

    /// <summary>Calculates the visible world rectangle for the current camera.</summary>
    /// <returns>The visible world rectangle.</returns>
    private Rect2 GetViewportRect()
    {
        var size = GetViewportWorldSize();
        return new Rect2(_camera.Position - (size / 2f), size);
    }

    /// <summary>Calculates the visible viewport size in world coordinates.</summary>
    /// <returns>The visible world size.</returns>
    private Vector2 GetViewportWorldSize() => _root.GetViewport().GetVisibleRect().Size / 3f;

    /// <summary>Updates exit-cue sprites from the current cue snapshot.</summary>
    /// <param name="state">Current field simulation state.</param>
    private void PresentCue(GameState state)
    {
        if (_cuePop is null || _cueInner is null)
        {
            throw new InvalidOperationException("cue sprites unavailable");
        }

        if (!_cueSnapshot.Visible)
        {
            _cuePop.Visible = false;
            _cueInner.Visible = false;
            return;
        }

        ApplyCueSprite(_cuePop, _cue.Frames[_cueSnapshot.PopFrame], state);
        if (_cueSnapshot.InnerFrame is int innerFrame)
        {
            ApplyCueSprite(_cueInner, _cue.Frames[innerFrame], state);
        }
        else
        {
            _cueInner.Visible = false;
        }
    }

    /// <summary>Applies one decoded exit-cue frame to its sprite.</summary>
    /// <param name="sprite">Sprite receiving the frame.</param>
    /// <param name="frame">Decoded cue frame to display.</param>
    /// <param name="state">Current field simulation state.</param>
    private void ApplyCueSprite(Sprite2D sprite, CueFrame frame, GameState state)
    {
        sprite.Texture = _textureCache.TextureFor(frame.Path);
        sprite.Position = new Vector2(state.Position.X + frame.OffsetX, state.Position.Y - 27 + frame.OffsetY);
        sprite.Visible = true;
    }

    /// <summary>Updates or hides the above-head connection icon.</summary>
    /// <param name="view">Optional icon view state.</param>
    private void PresentIcon(IconView? view)
    {
        if (_iconSprite is null)
        {
            return;
        }

        if (view is not { } iconView)
        {
            _iconSprite.Visible = false;
            return;
        }

        var frame = _iconData.Frames[iconView.Frame];
        _iconSprite.Texture = _textureCache.TextureFor(frame.Path);
        _iconSprite.Offset = new Vector2(frame.OffsetX, frame.OffsetY);
        _iconSprite.Position = new Vector2(iconView.Position.X, iconView.Position.Y);
        _iconSprite.Visible = true;
    }
}
