using Godot;
using Dw3Production.Conversion;
using Dw3Production.Simulation;

namespace Dw3Production.Presentation;

/// <summary>One side's fighter, as the HUD draws it.</summary>
public readonly record struct BattleFighterView(int Id, BattleTextLine? Name, int Hp, int MaxHp, int Mp, int MaxMp, bool Active, bool KnockedOut);

/// <summary>One partner's row in the victory report: what exp it gained, its level after the
/// growth, and whether it levelled up.</summary>
public readonly record struct BattlePartnerReportView(int Id, BattleTextLine? Name, int ExpGained, int Level, bool LeveledUp);

/// <summary>The victory report: the battle's real reward (exp share, money, digimonExp) and
/// each fought partner's exp. Null while the battle is not resolved.</summary>
public readonly record struct BattleReportView(BattleOutcome Outcome, int Exp, int Money, int DigimonExp, IReadOnlyList<BattlePartnerReportView> Partners, BattleTextCatalog Text);

/// <summary>The render view for one frame of the battle, computed by the host from the flow
/// controller. The models are posed and shown/hidden from this, and the HUD reads the rest.</summary>
public readonly record struct BattleView(
    BattleFlowPhase Phase,
    BattleOutcome Outcome,
    IReadOnlyList<BattleFighterView> Players,
    IReadOnlyList<BattleFighterView> Enemies,
    string Message,
    BattleMenuView? Menu,
    BattleReportView? Report = null,
    BattleTextLine? MessageText = null,
    bool MessageVisible = false);

/// <summary>
/// The battle presentation: a full-screen 3D sub-viewport (the decoded fight stage, the EXP-300
/// battle models over it, a battle camera that focuses the active fighter) plus a 2D HUD overlay
/// (disc-derived HP bars and command menu). It is read-only over the flow controller, exactly
/// like DialogueRenderer over the dialogue state.
/// </summary>
public sealed partial class BattleRenderer
{
    private const int ViewportWidth = 1280;
    private const int ViewportHeight = 720;


    private readonly CanvasLayer _layer;
    private readonly SubViewportContainer _screen;
    private readonly Node3D _world;
    private readonly Camera3D _camera;
    // the auxiliary fighter-view contract owns a separate 100x60 layer (0x1009). Godot cannot render one
    // Node3D into two cameras at independent sizes, so this world carries a second decoded
    // instance of the active player only, with the same source lights and pose.
    private readonly SubViewport _partnerViewport;
    private readonly Node3D _partnerWorld;
    private readonly Camera3D _partnerCamera;
    private readonly TextureRect _partnerScreen;
    private BattleModel? _partnerModel;
    private readonly WorldEnvironment _environment;
    private readonly BattleHudNode _hud;
    private readonly ColorRect _flash;
    private readonly Sprite3D _hitSpark;
    private readonly List<BattleModel> _models = [];
    private readonly HashSet<int> _probeHiddenFighters = [];
    private readonly BattleModelLayer _modelLayer;
    private readonly BattleModelLayer _partnerModelLayer;
    private (int Polygons, int Culled, int AbrUnsorted)? _lastOrderingLog;
    private readonly List<(MeshInstance3D Instance, int FighterId, bool Enemy)> _placeholders = [];
    private BattleModel? _stage;
    private string _lastModelLoadFailure = "unknown model loader failure";
    private Node3D? _floor;
    private bool _built;
    private bool _probePoseOverride;
    private int? _probeAnimationFrame;
    private float _motionPalFrames;
    private BattleModel.BattleLightSet? _stageLights;
    private int _entranceFade;
    private float _entrancePalFrames;
    private const int EntranceFadeFrames = 60;

    /// <summary>Current source stage fade (0..1), shared by lights, stage colour and background.</summary>
    public float LightFade => _entranceFade / 4096f;

    // The battle camera (the decoded battle-camera record contract / the decoded fighter-camera record contract): source code negates FighterInfo
    // Y/Z when constructing GsRVIEW2, then the PS1-to-Godot world conversion negates them again.
    // The stored camera triples therefore map at /256 without another sign change.
    private Vector3 _camPos = new(0, 6.5f, 22f);
    private Vector3 _camTarget = new(0, -0.4f, 0);
    private Vector3 _camPosTo = new(0, 6.5f, 22f);
    private Vector3 _camTargetTo = new(0, -0.4f, 0);
    private Vector3 _camPosFrom = new(0, 6.5f, 22f);
    private Vector3 _camTargetFrom = new(0, -0.4f, 0);
    private float _camProj = 280f;
    private float _camProjFrom = 280f;
    private float _camProjTo = 280f;
    private float _camFade = 1f;
    private float _camFadeStep = 1f;
    private BattleShotCamera? _shotCamera;
    private readonly IShotCameraRng _shotRng = new RuntimeShotRng();

    public BattleRenderer(Node root)
    {
        _layer = new CanvasLayer { Layer = 300, Visible = false };
        root.AddChild(_layer);

        var viewport = new SubViewport
        {
            Size = new Vector2I(ViewportWidth, ViewportHeight),
            RenderTargetUpdateMode = SubViewport.UpdateMode.WhenVisible,
            OwnWorld3D = true,
            HandleInputLocally = false,
        };

        _world = new Node3D();
        viewport.AddChild(_world);
        _camera = new Camera3D
        {
            Position = new Vector3(0, 6.5f, 22f),
            RotationDegrees = new Vector3(-4, 0, 0),
            Current = true,
            KeepAspect = Camera3D.KeepAspectEnum.Height,
            Fov = BattleSceneMath.VerticalFovDegrees(280),
        };
        _world.AddChild(_camera);
        // No Godot lighting is attached to the battle world. Model and stage meshes are
        // unshaded and receive only the explicit the decoded stage-light data implementation in BattleModel.
        _environment = new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color(0.05f, 0.07f, 0.13f),
                AmbientLightSource = Godot.Environment.AmbientSource.Disabled,
            },
        };
        _world.AddChild(_environment);
        AddFloor();
        _modelLayer = new BattleModelLayer(_world, "BattleModelOrderingLayer");

        _partnerViewport = new SubViewport
        {
            Size = new Vector2I(100, 60),
            TransparentBg = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            OwnWorld3D = true,
            HandleInputLocally = false,
        };
        _partnerWorld = new Node3D { Name = "PartnerViewWorld" };
        _partnerViewport.AddChild(_partnerWorld);
        _partnerModelLayer = new BattleModelLayer(_partnerWorld, "PartnerModelOrderingLayer");
        _partnerCamera = new Camera3D { Current = true, KeepAspect = Camera3D.KeepAspectEnum.Height };
        _partnerWorld.AddChild(_partnerCamera);
        _partnerScreen = new TextureRect
        {
            Texture = _partnerViewport.GetTexture(),
            Position = new Vector2(208 * BattleHudLayout.Scale + BattleHudLayout.OffsetX, 76 * BattleHudLayout.Scale),
            Size = new Vector2(100 * BattleHudLayout.Scale, 60 * BattleHudLayout.Scale),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
        };
        _layer.AddChild(_partnerViewport);
        _layer.AddChild(_partnerScreen);

        _screen = new SubViewportContainer { Stretch = true };
        _screen.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _screen.AddChild(viewport);
        _layer.AddChild(_screen);

        // PS1 320x240 -> 1280x720 at exact 3x with 160px side margins, matching dialogue.
        _hud = new BattleHudNode
        {
            Scale = new Vector2(BattleHudLayout.Scale, BattleHudLayout.Scale),
            Position = new Vector2(BattleHudLayout.OffsetX, BattleHudLayout.OffsetY),
        };
        _layer.AddChild(_hud);

        // The white-flash overlay (the full-screen flash layer: a full-screen quad added to the front layer)
        // and the hit spark (a small emissive diamond at the struck fighter).
        _flash = new ColorRect { Color = new Color(1, 1, 1, 0), MouseFilter = Control.MouseFilterEnum.Ignore };
        _flash.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _layer.AddChild(_flash);
        // the render-layer contract:createLayer takes the first free slot, so the auxiliary fighter-view contract's 0x1009 is created
        // after FIGHTSTG_CREATE_LAYERS' SCREEN/MENUS/FRONT layers. It must draw over frame 10.
        _layer.MoveChild(_partnerScreen, _layer.GetChildCount() - 1);
        _ = new QuadMesh { Size = new Vector2(1.4f, 1.4f) };
        _hitSpark = new Sprite3D
        {
            Texture = MakeSparkTexture(),
            Position = Vector3.Zero,
            Modulate = new Color(1f, 1f, 0.7f, 0.95f),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            PixelSize = 0.05f,
            Visible = false,
        };
        _world.AddChild(_hitSpark);
    }

    /// <summary>A procedural spark sprite: the stand-in for the source hit-effect sheet
    /// (the decoded sprite-effect sheet effect 0x33), which is a decoded-sheet gap.</summary>
    private static ImageTexture MakeSparkTexture()
    {
        var image = Image.CreateEmpty(16, 16, false, Image.Format.Rgba8);
        for (var y = 0; y < 16; y++)
            for (var x = 0; x < 16; x++)
            {
                var dx = x - 7.5f;
                var dy = y - 7.5f;
                var d = Mathf.Sqrt(dx * dx + dy * dy);
                var alpha = Mathf.Clamp(1f - d / 7.5f, 0f, 1f);
                image.SetPixel(x, y, new Color(1, 1, 0.85f, alpha));
            }
        return ImageTexture.CreateFromImage(image);
    }

    private void AddFloor()
    {
        // A dark floor under the models. The decoded fight stage replaces this visually, but a
        // neutral floor stays as the fallback (and to catch shadowing) when a stage is absent.
        _floor = new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(60, 40) },
            Position = new Vector3(0, -1.4f, -4),
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.08f, 0.09f, 0.13f),
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            },
        };
        _world.AddChild(_floor);
    }

    /// <summary>True once at least one battle model is built and the scene is showing.</summary>
    public bool Built => _built;

    public void Show() => _layer.Visible = true;
    public void Hide() => _layer.Visible = false;
    public bool Visible => _layer.Visible;

    /// <summary>Probe-only HUD suppression for a model/stage-only framebuffer comparison.</summary>
    public void SetHudVisibleForProbe(bool visible) => _hud.Visible = visible;

    /// <summary>Probe oracle for the source HUD mapping and command cursor bounds.</summary>
    public string AssertHudLayout() => _hud.AssertLayout();

    /// <summary>Loads the decoded fight stage (the battle background) for the encounter's
    /// <paramref name="stageId"/> and poses it statically. Returns false (and logs) when the
    /// JSON is absent or invalid; the dark floor then stays as the background, never silently
    /// presented as the real stage.</summary>
    public bool BuildStage(string stageJsonPath, int stageId)
    {
        try
        {
            _stage?.Root.QueueFree();
            _stage = BattleModel.Load(_world, stageJsonPath);
            // FIGHTSTG_createStage leaves the plain model control at identity. Model vertices,
            // bones, fighters and camera all share the same /256 PS1-to-Godot conversion.
            _stage.Root.Position = Vector3.Zero;
            _stage.Root.Scale = Vector3.One;
            _stageLights = _stage.StageLights ?? throw new InvalidOperationException("fight stage has no LightSet");
            RestartEntranceFade();
            // The stage is a plain model (FIGHTSTG_createPlainModel): one motion, no idle loop.
            // Pose it at its motion's first keyframe as a static arena.
            var keyframes = _stage.MotionKeyframes(0);
            _stage.Pose(keyframes.Count > 0 ? keyframes[0] : 0);
            if (_floor is { } floor) floor.Visible = false;
            return true;
        }
        catch (Exception error) when (error is InvalidOperationException or FileNotFoundException or System.Text.Json.JsonException)
        {
            GD.PushWarning($"WARN battle-stage-load-failed stage={stageId} reason={error.Message}");
            _stage = null;
            return false;
        }
    }

    /// <summary>True once a decoded fight stage is loaded and drawn as the background.</summary>
    public bool StageBuilt => _stage is not null;

    /// <summary>Builds the active player's model on the left and the real enemy model on the right.
    /// The enemy model comes from the disc model decoder (same EXP-300 chain, parameterised per
    /// fighter); if its archive is not decodable a clearly distinct capsule stands in and is
    /// explicitly logged, never silently presented as the real model.</summary>
    public void BuildModels(string playerModelJsonPath, string enemyModelJsonPath, string playerLabel, string enemyLabel, int enemyFighterId)
    {
        ClearModels();
        var playerFighterId = ParseFighterId(playerModelJsonPath);
        if (!TryAddModel(playerModelJsonPath, playerLabel, playerFighterId, enemy: false))
        {
            GD.PushWarning($"WARN battle-player-model-undecoded fighter={playerFighterId} capsule-stand-in label={playerLabel}");
            AddPlaceholder(playerFighterId, playerLabel, enemy: false, new Color(0.1f, 0.3f, 0.75f));
        }

        TryBuildPartnerView(playerModelJsonPath, playerFighterId);

        if (!TryAddModel(enemyModelJsonPath, enemyLabel, enemyFighterId, enemy: true))
        {
            // The enemy archive is not decodable (missing or malformed conversion): the capsule
            // is an explicit, logged fallback, never a silent stand-in.
            GD.PushWarning($"WARN battle-enemy-model-undecoded fighter={enemyFighterId} capsule-stand-in label={enemyLabel}");
            AddPlaceholder(enemyFighterId, enemyLabel, enemy: true, new Color(0.6f, 0.1f, 0.2f));
        }
        _built = true;
    }

    /// <summary>Builds exactly one source fighter for the offline contact sheet. It uses the same
    /// loader, idle pose, stage lighting and source camera as presentation, but removes the
    /// opponent and HUD so every tile remains an inspectable fighter render.</summary>
    public void BuildSingleModelForSheet(string modelJsonPath, string label, int fighterId, bool enemy)
    {
        ClearModels();
        if (!TryAddModel(modelJsonPath, label, fighterId, enemy))
            throw new InvalidOperationException($"sheet fighter={fighterId} model load failed: {_lastModelLoadFailure}");
        _built = true;
        _hud.Visible = false;
        _flash.Visible = false;
        _hitSpark.Visible = false;
    }

    /// <summary>Leaves only stage 18 for the contact-sheet background mask.</summary>
    public void ClearModelsForSheet()
    {
        ClearModels();
        _hud.Visible = false;
        _flash.Visible = false;
        _hitSpark.Visible = false;
    }

    private static int ParseFighterId(string path)
    {
        const string prefix = "fighter-";
        const string suffix = "-model";
        var name = Path.GetFileNameWithoutExtension(path);
        if (!name.StartsWith(prefix, StringComparison.Ordinal) || !name.EndsWith(suffix, StringComparison.Ordinal)) return -1;
        var token = name[prefix.Length..^suffix.Length];
        return int.TryParse(token, out var fighter) ? fighter : -1;
    }

    /// <summary>Loads one decoded battle model at a side; returns false (and logs nothing here)
    /// when the JSON is absent or invalid, so the caller can fall back to a capsule.</summary>
    private bool TryAddModel(string jsonPath, string label, int fighterId, bool enemy)
    {
        try
        {
            var model = BattleModel.Load(_world, jsonPath);
            model.Root.Name = $"{label}#{fighterId}";
            var placement = BattleSceneMath.FighterPlacement(enemy, model.Distance, model.Height);
            model.Root.Position = new Vector3(placement.Position.X, placement.Position.Y, placement.Position.Z);
            model.Root.Rotation = new Vector3(0f, placement.YawRadians, 0f);
            model.Root.Scale = Vector3.One;
            model.Step(0);
            _models.Add(model);
            ApplyStageLights();
            GD.Print($"BATTLE_GEOMETRY fighter={fighterId} height={model.Height} distance={model.Distance} " +
                $"position=({model.Root.Position.X:0.###},{model.Root.Position.Y:0.###},{model.Root.Position.Z:0.###}) " +
                $"rotation=(0,{placement.YawRadians:0.###},0)");
            return true;
        }
        catch (Exception error) when (error is InvalidOperationException or FileNotFoundException or System.Text.Json.JsonException)
        {
            _lastModelLoadFailure = error.Message;
            GD.PushWarning($"WARN battle-model-load-failed fighter={fighterId} enemy={enemy} reason={error.Message}");
            return false;
        }
    }

    private void TryBuildPartnerView(string jsonPath, int fighterId)
    {
        try
        {
            _partnerModel = BattleModel.Load(_partnerWorld, jsonPath);
            _partnerModel.Root.Name = $"PartnerView#{fighterId}";
            var placement = BattleSceneMath.FighterPlacement(enemy: false, _partnerModel.Distance, _partnerModel.Height);
            _partnerModel.Root.Position = new Vector3(placement.Position.X, placement.Position.Y, placement.Position.Z);
            _partnerModel.Root.Rotation = new Vector3(0f, placement.YawRadians, 0f);
            _partnerModel.Step(0);
            ApplyStageLights();
        }
        catch (Exception error) when (error is InvalidOperationException or FileNotFoundException or System.Text.Json.JsonException)
        {
            _partnerModel = null;
            GD.PushWarning($"WARN battle-partner-view-model-failed fighter={fighterId} reason={error.Message}");
        }
    }

    /// <summary>the decoded fighter-camera record contract reloads the layer 0x1009 view from 6 normally or 7 while
    /// the player model's live ModelControl.idleMotion is non-zero. the auxiliary fighter-view contract gives that
    /// layer a 100x60 target, so camProj maps to a 30-pixel vertical half-height. The source
    /// writes negated Y/Z into GsRVIEW2, and Godot's PS1 conversion negates them back exactly
    /// as the main battle camera does.</summary>
    private void SetPartnerCamera()
    {
        if (_partnerModel is null)
        {
            _partnerScreen.Visible = false;
            return;
        }
        // the auxiliary fighter-view contract puts the player's existing model on layer 0x1009. Our target owns a
        // duplicate mesh solely for its separate viewport, but camera 6/7 must follow the live
        // motion controller of _models[0], not that copy's independent playback state.
        var playerIdleMotion = _models.Count > 0 ? _models[0].Motion?.IdleMotion ?? 0 : 0;
        var viewIndex = BattleSceneMath.PartnerCameraViewIndex(playerIdleMotion);
        if (_partnerModel.Cameras.Count <= viewIndex)
        {
            _partnerScreen.Visible = false;
            return;
        }
        var view = _partnerModel.Cameras[viewIndex];
        var position = BattleSceneMath.CameraPosition(view.CamPos[0], view.CamPos[1], view.CamPos[2]);
        var reference = BattleSceneMath.CameraPosition(view.CamRef[0], view.CamRef[1], view.CamRef[2]);
        _partnerCamera.Position = new Vector3(position.X, position.Y, position.Z);
        _partnerCamera.LookAt(new Vector3(reference.X, reference.Y, reference.Z), Vector3.Up);
        _partnerCamera.Fov = BattleSceneMath.VerticalFovDegrees(view.Proj, 30f);
    }

    /// <summary>Opaque pixels in the dedicated 100x60 source render target. This is deliberately
    /// independent from the main framebuffer, whose stage lies behind the thumbnail rectangle.</summary>
    public int PartnerViewOpaquePixels()
    {
        if (!_partnerScreen.Visible) return 0;
        var image = _partnerViewport.GetTexture().GetImage();
        var count = 0;
        for (var y = 0; y < image.GetHeight(); y++) for (var x = 0; x < image.GetWidth(); x++)
                if (image.GetPixel(x, y).A8 > 0) count++;
        return count;
    }

    /// <summary>Writes the raw 100x60 target for visual probe evidence.</summary>
    public Error SavePartnerView(string path) => _partnerViewport.GetTexture().GetImage().SavePng(path);

    /// <summary>Probe-only visibility toggle for the independent 0x1009 partner model.</summary>
    public void SetPartnerModelVisibleForProbe(bool visible)
    {
        if (_partnerModel is null) throw new InvalidOperationException("partner view model is not built");
        _partnerModel.Root.Visible = visible;
    }

    private void AddPlaceholder(int fighterId, string label, bool enemy, Color color)
    {
        var instance = new MeshInstance3D
        {
            Name = $"{label}#{fighterId}",
            Mesh = new CapsuleMesh { Radius = 0.9f, Height = 3.2f },
            Position = new Vector3(0, 0, enemy ? -20 : 20),
            Scale = Vector3.One,
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = color,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            },
        };
        _world.AddChild(instance);
        _placeholders.Add((instance, fighterId, enemy));
    }

    /// <summary>The number of real battle models built (player + any decoded enemy).</summary>
    public int ModelCount => _models.Count;

    /// <summary>Poses every built model at <paramref name="frame"/> (for the probe's distinct-pose proof).</summary>
    public void PoseAll(int frame)
    {
        foreach (var model in _models) model.Pose(frame);
    }

    /// <summary>Source scale-threshold status for an actual decoded model, used by the offscreen
    /// regression probe rather than inferred from its final pixels.</summary>
    public string BoneVisibility(int fighterId)
    {
        var model = _models.FirstOrDefault(candidate => candidate.FighterId == fighterId)
            ?? throw new InvalidOperationException($"battle model fighter={fighterId} is not built");
        return $"fighter={fighterId} hidden=[{string.Join(",", model.HiddenBones)}]";
    }

    /// <summary>Probe: (opaque, translucent) surface counts for a fighter, or for the stage
    /// when <paramref name="fighterId"/> is negative.</summary>
    public (int Opaque, int Translucent) SurfacePasses(int fighterId)
    {
        var model = fighterId < 0 ? _stage : _models.FirstOrDefault(candidate => candidate.FighterId == fighterId);
        if (model is null) return (0, 0);
        var opaque = model.Materials.Count(BattleModel.IsOpaqueMaterial);
        return (opaque, model.Materials.Count - opaque);
    }

    /// <summary>Whether an actual decoded bone survived the source scale threshold.</summary>
    public bool BoneVisible(int fighterId, int bone) => _models.FirstOrDefault(candidate => candidate.FighterId == fighterId)?.IsBoneVisible(bone) == true;

    /// <summary>Maximum matrix/scale difference between ApplyPose and the explicit PS1 ZYX oracle.</summary>
    public float RotationOracleMaxDiff() => _models.Count == 0 ? float.PositiveInfinity : _models.Max(model => model.RotationOracleMaxDiff());

    /// <summary>Screen-winding versus source-normal agreement for a decoded fighter.</summary>
    public (int Total, int Agree) WindingAgreement(int fighterId)
    {
        var model = _models.FirstOrDefault(candidate => candidate.FighterId == fighterId)
            ?? throw new InvalidOperationException($"battle model fighter={fighterId} is not built");
        return model.WindingAgreement(_camera);
    }

    public string FaceSummary(int fighterId) => _models.FirstOrDefault(candidate => candidate.FighterId == fighterId)?.FaceSummary()
        ?? throw new InvalidOperationException($"battle model fighter={fighterId} is not built");

    public int FaceChangedTexels(int fighterId) => _models.FirstOrDefault(candidate => candidate.FighterId == fighterId)?.FaceChangedTexels()
        ?? throw new InvalidOperationException($"battle model fighter={fighterId} is not built");

    public void SetFaceRandomForProbe(int fighterId, int randomValue)
    {
        var model = _models.FirstOrDefault(candidate => candidate.FighterId == fighterId)
            ?? throw new InvalidOperationException($"battle model fighter={fighterId} is not built");
        model.SetFaceRandomForProbe(() => randomValue);
    }

    public void SetFaceBlinkTimerForProbe(int fighterId, int timer)
    {
        var model = _models.FirstOrDefault(candidate => candidate.FighterId == fighterId)
            ?? throw new InvalidOperationException($"battle model fighter={fighterId} is not built");
        model.SetFaceBlinkTimerForProbe(timer);
        model.Step(0);
    }

    /// <summary>Probe-only baseline resembling the old identity-scale renderer. The next PoseAll
    /// restores source construction before any ordinary presentation continues.</summary>
    public void ShowAllBonesForProbe()
    {
        foreach (var model in _models) model.ShowAllBonesForProbe();
        _probePoseOverride = true;
    }

    /// <summary>Probe-only visibility toggle for one decoded fighter in a framebuffer mask.</summary>
    public void SetModelVisibleForProbe(int fighterId, bool visible)
    {
        var model = _models.FirstOrDefault(candidate => candidate.FighterId == fighterId)
            ?? throw new InvalidOperationException($"battle model fighter={fighterId} is not built");
        model.SetVisibleForProbe(visible);
        if (visible) _probeHiddenFighters.Remove(fighterId);
        else _probeHiddenFighters.Add(fighterId);
        // CaptureBattle does not necessarily traverse Present while a probe freezes the pose.
        // Rebuild synchronously so the ordering mesh follows the probe's visibility mask.
        _modelLayer.Rebuild(_camera, _models);
    }

    /// <summary>Holds a deterministic source frame across an offscreen capture. Ordinary battle
    /// presentation keeps its time-driven pose path because this is only called by a probe.</summary>
    public void SetProbePose(int frame)
    {
        PoseAll(frame);
        _probePoseOverride = true;
    }

    /// <summary>Pins HUD animation to a deterministic 16 ms source frame for an offscreen probe.</summary>
    public void SetProbeAnimationFrame(int? frame)
    {
        _probeAnimationFrame = frame is null ? null : ValidateProbeAnimationFrame(frame.Value);
        _hud.SetProbeAnimationMilliseconds(_probeAnimationFrame is { } pinned ? ProbeAnimationMilliseconds(pinned) : null);
    }

    /// <summary>Maps a deterministic HUD frame to its source animation milliseconds.</summary>
    public static ulong ProbeAnimationMilliseconds(int frame) => (ulong)ValidateProbeAnimationFrame(frame) * 16UL;

    private static int ValidateProbeAnimationFrame(int frame)
    {
        if (frame < 0) throw new ArgumentOutOfRangeException(nameof(frame));
        return frame;
    }

    /// <summary>Probe-only culling baseline. Callers must restore Back before leaving the probe.</summary>
    public void SetCullModeForProbe(BaseMaterial3D.CullModeEnum mode)
    {
        foreach (var model in _models) model.SetCullModeForProbe(mode);
    }

    /// <summary>Poses every built model at motion <paramref name="motionIndex"/>'s keyframe
    /// <paramref name="frameInMotion"/> (for the probe's attack-pose proof).</summary>
    public void PoseMotionAll(int motionIndex, int frameInMotion)
    {
        foreach (var model in _models)
        {
            var keyframes = model.MotionKeyframes(motionIndex);
            if (keyframes.Count == 0) continue;
            model.Pose(keyframes[Mathf.Clamp(frameInMotion, 0, keyframes.Count - 1)]);
        }
    }

    /// <summary>Requests a one-based source motion for the active player model.</summary>
    public void SetPlayerMotion(int motion, bool restart = true)
    {
        if (_models.Count > 0) _models[0].SetMotion(motion, restart);
    }

    /// <summary>Requests a recovered source motion only when the selected model carries it.
    /// A missing motion is surfaced to the host log rather than substituted with a guess.</summary>
    public bool TrySetPlayerMotion(int motion, bool restart = true)
    {
        if (_models.Count == 0 || _models[0].Motions.Count < motion) return false;
        _models[0].SetMotion(motion, restart);
        return true;
    }

    /// <summary>Requests one-based source motion numbers for all real fighter models.</summary>
    public void SetMotionAll(int motion, bool restart = true)
    {
        foreach (var model in _models) model.SetMotion(motion, restart);
    }

    /// <summary>Maps 60 Hz presentation time to the source PAL 50 Hz clock without drift.</summary>
    public void StepMotions(float delta)
    {
        _motionPalFrames += delta * 50f;
        var elapsed = (int)_motionPalFrames;
        if (elapsed == 0) return;
        _motionPalFrames -= elapsed;
        foreach (var model in _models) model.Step(elapsed);
        _partnerModel?.Step(elapsed);
    }

    /// <summary>Source the battle presentation flow creates the stage with a 60-frame fade. the battle-stage presentation contract and the decoded stage-light data use
    /// the same PAL clock for the light set, model colour and SCREEN_LAYER background.</summary>
    private void StepEntranceFade(float delta)
    {
        if (_stageLights is null || _entranceFade >= 0x1000) return;
        _entrancePalFrames += delta * 50f;
        var frames = (int)_entrancePalFrames;
        if (frames == 0) return;
        _entrancePalFrames -= frames;
        _entranceFade = BattleSceneMath.AdvanceStageFade(_entranceFade,
            BattleSceneMath.StageFadeStep(EntranceFadeFrames), frames);
        ApplyEntranceFade();
    }

    private void ApplyStageLights()
    {
        if (_stageLights is not { } source) return;
        var fade = _entranceFade / 4096f;
        var set = new BattleModel.BattleLightSet([.. source.Lights.Select(light =>
            new BattleModel.BattleLight(light.Direction, light.Rgb * fade))], source.Ambient * fade);
        foreach (var model in _models) model.SetLighting(set);
        _partnerModel?.SetLighting(set);
        _stage?.SetLighting(set);
    }

    /// <summary>the battle-stage presentation contract FIGHTSTG_fadeStage: colorMode 1 reaches 0x80 and black SCREEN_LAYER
    /// writes 1,1,1 rather than literal zero.</summary>
    private void ApplyEntranceFade()
    {
        ApplyStageLights();
        var level = BattleSceneMath.LerpFixed(0, 0x80, _entranceFade) / 255f;
        _stage?.SetModelColor(new Color(level, level, level));
        var target = _stage?.StageBgColor ?? Colors.Black;
        var bg = new Color(
            BattleSceneMath.LerpFixed(0, (int)(target.R * 255), _entranceFade) / 255f,
            BattleSceneMath.LerpFixed(0, (int)(target.G * 255), _entranceFade) / 255f,
            BattleSceneMath.LerpFixed(0, (int)(target.B * 255), _entranceFade) / 255f);
        _environment.Environment.BackgroundColor = bg.R + bg.G + bg.B == 0f
            ? new Color(1f / 255f, 1f / 255f, 1f / 255f) : bg;
    }

    /// <summary>Each battle creates a stage task in the battle-stage presentation contract, including repeated fights on one stage.</summary>
    public void RestartEntranceFade()
    {
        _entranceFade = 0;
        _entrancePalFrames = 0f;
        ApplyEntranceFade();
    }

    /// <summary>Probe-only comparison against the same pose with GsSetFlatLight and BackColor neutralised.</summary>
    public void SetNeutralLightsForProbe(bool neutral)
    {
        if (neutral)
        {
            var off = new BattleModel.BattleLightSet([
                new BattleModel.BattleLight(Vector3.Zero, Colors.Black),
                new BattleModel.BattleLight(Vector3.Zero, Colors.Black),
                new BattleModel.BattleLight(Vector3.Zero, Colors.Black)], Colors.Black);
            // Keep the stage rendered with its own LightSet. The comparison then isolates the
            // fighter silhouette instead of turning the whole background into a second variable.
            foreach (var model in _models) model.SetLighting(off);
            return;
        }
        _entranceFade = 0x1000;
        ApplyEntranceFade();
    }

    /// <summary>Recreates a measured point of the source entrance fade for a visual oracle.
    /// Production always advances this value in Present.</summary>
    public void SetLightFadeForProbe(float fade)
    {
        _entranceFade = (int)(Mathf.Clamp(fade, 0f, 1f) * 0x1000);
        _entrancePalFrames = 0f;
        ApplyEntranceFade();
    }

    public string StageLightSummary()
    {
        if (_stageLights is not { } set) throw new InvalidOperationException("stage lights unavailable");
        var rgb = set.Lights.Select(light => $"dir=({light.Direction.X:0.###},{light.Direction.Y:0.###},{light.Direction.Z:0.###}) rgb=({light.Rgb.R * 255:0},{light.Rgb.G * 255:0},{light.Rgb.B * 255:0})");
        return $"lights={set.Lights.Count} ambient=({set.Ambient.R * 255:0},{set.Ambient.G * 255:0},{set.Ambient.B * 255:0}) {string.Join(" ", rgb)}";
    }

    /// <summary>One decoded lit vertex after the same GTE-style lighting calculation used by
    /// the visible mesh. This is diagnostic evidence, never a rendering shortcut.</summary>
    public string LightingVertexSummary(int fighterId) =>
        _models.FirstOrDefault(model => model.FighterId == fighterId)?.LightingVertexSummary()
        ?? throw new InvalidOperationException($"battle model fighter={fighterId} is not built");

    /// <summary>Sets the battle camera to a decoded source view, fading from where it is.
    /// the decoded battle-camera record contract's GsRVIEW2 sign change and the world-coordinate conversion cancel, so
    /// decoded camPos/camRef map directly at the shared /256 scale.</summary>
    public void SetSourceCamera(BattleModel.BattleCameraView view, int frames = 60, bool snap = false)
    {
        _camPosFrom = _camPos;
        _camTargetFrom = _camTarget;
        _camProjFrom = _camProj;
        // the decoded battle-camera record contract first negates the FighterInfo Y/Z values into GsRVIEW2. Converting
        // that PS1 view into Godot negates Y/Z again, so the stored raw components remain signed
        // exactly as decoded here. This is the same world conversion the models use.
        var position = BattleSceneMath.CameraPosition(view.CamPos[0], view.CamPos[1], view.CamPos[2]);
        var reference = BattleSceneMath.CameraPosition(view.CamRef[0], view.CamRef[1], view.CamRef[2]);
        _camPosTo = new Vector3(position.X, position.Y, position.Z);
        _camTargetTo = new Vector3(reference.X, reference.Y, reference.Z);
        _camProjTo = view.Proj;
        _camFade = snap ? 1f : 0f;
        _camFadeStep = frames <= 0 ? 1f : 1f / frames;
        if (snap)
        {
            _camPos = _camPosTo;
            _camTarget = _camTargetTo;
            _camProj = _camProjTo;
        }
        ApplyCamera();
        GD.Print($"BATTLE_CAMERA pos=({view.CamPos[0]},{view.CamPos[1]},{view.CamPos[2]}) " +
            $"ref=({view.CamRef[0]},{view.CamRef[1]},{view.CamRef[2]}) proj={view.Proj} " +
            $"fov={BattleSceneMath.VerticalFovDegrees(view.Proj):0.###} frames={(snap ? 0 : frames)}");
    }

    /// <summary>Applies the complete CameraView composed as the decoded battle-camera record contract:84-102 does before
    /// GsSetRefView2. This is the path used by the player command ShotCamera.</summary>
    public void SetSourceCamera(SourceBattleCameraView view, bool snap = true)
    {
        _camPosFrom = _camPos;
        _camTargetFrom = _camTarget;
        _camProjFrom = _camProj;
        var position = BattleSceneMath.ComposeCameraPosition(view, reference: false);
        var reference = BattleSceneMath.ComposeCameraPosition(view, reference: true);
        _camPosTo = new Vector3(position.X, position.Y, position.Z);
        _camTargetTo = new Vector3(reference.X, reference.Y, reference.Z);
        _camProjTo = view.Projection;
        _camFade = snap ? 1f : 0f;
        _camFadeStep = 1f;
        if (snap) { _camPos = _camPosTo; _camTarget = _camTargetTo; _camProj = _camProjTo; }
        ApplyCamera();
    }

    /// <summary>The battle camera's interpolation (FIGHTSTG_fadeBattleCamera): advances the
    /// fade and applies the interpolated view. Called every presented frame.</summary>
    public void StepCamera(float delta)
    {
        if (_camFade < 1f)
        {
            _camFade = Mathf.Min(1f, _camFade + _camFadeStep * delta * 60f);
            var t = _camFade;
            _camPos = _camPosFrom.Lerp(_camPosTo, t);
            _camTarget = _camTargetFrom.Lerp(_camTargetTo, t);
            _camProj = Mathf.Lerp(_camProjFrom, _camProjTo, t);
        }
        ApplyCamera();
    }

    private void ApplyCamera()
    {
        _camera.Position = _camPos;
        _camera.LookAt(_camTarget, Vector3.Up);
        _camera.Fov = BattleSceneMath.VerticalFovDegrees(Math.Max(1, (int)MathF.Round(_camProj)));
    }

    /// <summary>Loads the decoded battle-HUD frames (HP-bar caps, background, technique gauge,
    /// cursor) so the HUD draws the real disc sprites instead of a flat panel.</summary>
    public void SetHud(BattleHudData hud)
    {
        _hud.SetHud(hud);
    }

    /// <summary>Binds the CMFONTCS Technique/Item icons and message continuation arrow.</summary>
    public void SetMenuIcons(BattleMenuIconsData icons)
    {
        _hud.SetMenuIcons(icons);
    }

    /// <summary>Uses the decoded CMFONTTM atlas already used by DialogueRenderer.</summary>
    public void SetFont(FontData font, Texture2D page0, Texture2D page1) => _hud.SetFont(font, page0, page1);

    /// <summary>The decoded camera views of the player's (enemy: false) or enemy's (enemy: true)
    /// model, for the source camera. Empty when that model was not built.</summary>
    public IReadOnlyList<BattleModel.BattleCameraView> FighterCameras(bool enemy)
    {
        var model = enemy ? (_models.Count > 1 ? _models[1] : null) : (_models.Count > 0 ? _models[0] : null);
        return model?.Cameras ?? [];
    }

    /// <summary>Source polygons whose 0x9 command leaves residual textured primitive state.</summary>
    public int UntexturedPolygonCount(int fighterId) =>
        _models.FirstOrDefault(model => model.FighterId == fighterId)?.UntexturedPolygonCount ?? 0;

    /// <summary>the battle presentation flow starts on FIGHTSTG_getEnemyView. The source first reads the enemy's
    /// cameraCount, then deliberately calls getFighterView for model slot 0, so the actual view
    /// is the player's camera at enemyCameraCount - 1.</summary>
    public void SetOpeningCamera()
    {
        var playerCameras = FighterCameras(enemy: false);
        var enemyCameras = FighterCameras(enemy: true);
        var index = enemyCameras.Count - 1;
        if (index < 0 || index >= playerCameras.Count)
            throw new InvalidOperationException("source opening camera unavailable");
        SetSourceCamera(playerCameras[index], snap: true);
    }

    /// <summary>Sets the main command-menu scene camera. the player-turn presentation flow:19-22 creates both the
    /// small partner view and ShotCamera. The latter owns the full battle scene: it starts on
    /// `getEnemyView` (shot-camera selection:124-132), which calls player camera `enemyCameraCount - 1`
    /// (the decoded battle-camera record contract:187-205). For encounter 200 that is Kotemon view 2. Camera 6/7 belong
    /// only to the small partner-view layer, so using them for the full viewport hides Kuwagamon.</summary>
    public void SetCommandMenuCamera(bool snap = false)
    {
        _ = snap;
        var players = FighterCameras(enemy: false);
        var enemies = FighterCameras(enemy: true);
        if (players.Count == 0 || enemies.Count == 0) throw new InvalidOperationException("source shot camera unavailable");
        SourceBattleCameraView ViewFor(bool enemy, int index)
        {
            var cameras = enemy ? enemies : players;
            if (index < 0 || index >= cameras.Count) throw new InvalidOperationException($"source camera index={index} unavailable enemy={enemy}");
            var source = cameras[index];
            return new SourceBattleCameraView
            {
                Vpx = source.CamPos[0],
                Vpy = source.CamPos[1],
                Vpz = source.CamPos[2],
                Vrx = source.CamRef[0],
                Vry = source.CamRef[1],
                Vrz = source.CamRef[2],
                Projection = source.Proj,
            };
        }
        _shotCamera = new BattleShotCamera(_shotRng, new BattleShotCameraViews
        {
            GetEnemyView = () => ViewFor(enemy: false, enemies.Count - 1),
            GetFighterView = (slot, index) => ViewFor(enemy: slot != 0, index),
        });
        SetSourceCamera(_shotCamera.View, snap: true);
        ApplyShotVisibility();
    }

    public string ShotCameraSummary() => _shotCamera is null ? "inactive" :
        $"list={_shotCamera.List} shot={_shotCamera.Shot} substate={_shotCamera.Substate} rot.vy=0x{_shotCamera.View.RotationY:X3} time={_shotCamera.Time} player={_shotCamera.PlayerVisible} enemy={_shotCamera.EnemyVisible}";

    public void StepShotCamera()
    {
        if (_shotCamera is null) return;
        _shotCamera.Step();
        SetSourceCamera(_shotCamera.View, snap: true);
        ApplyShotVisibility();
    }

    public void StopShotCamera()
    {
        _shotCamera?.Stop();
        _shotCamera = null;
        foreach (var model in _models) model.Root.Visible = !_probeHiddenFighters.Contains(model.FighterId);
    }

    private void ApplyShotVisibility()
    {
        if (_shotCamera is null) return;
        if (_models.Count > 0) _models[0].Root.Visible = _shotCamera.PlayerVisible && !_probeHiddenFighters.Contains(_models[0].FighterId);
        if (_models.Count > 1) _models[1].Root.Visible = _shotCamera.EnemyVisible && !_probeHiddenFighters.Contains(_models[1].FighterId);
    }

    private sealed class RuntimeShotRng : IShotCameraRng
    {
        public int Next() => Random.Shared.Next();
    }

    /// <summary>Offscreen acceptance oracle for a source battle view. The opening view validates
    /// both fighters. A command-idle view validates its selected active fighter, since the source
    /// player camera deliberately frames that fighter rather than the opposing full AABB.</summary>
    public string AssertOpeningGeometry(int? focusedFighterId = null)
    {
        if (_models.Count != 2 || _stage is null)
            throw new InvalidOperationException("opening geometry requires two real fighters and a stage");
        var summaries = new List<string>();
        foreach (var model in _models)
        {
            if (focusedFighterId is not null && model.FighterId != focusedFighterId.Value) continue;
            // The ordering-table renderer owns visibility. MeshInstance3D nodes are source
            // geometry carriers and may all be hidden while their polygons remain visible.
            var bounds = WorldBounds(model.Root, includeInvisible: true, out var rawMeshes, out _);
            var visibleMeshes = rawMeshes;
            var excludedMeshes = 0;
            var rawBounds = bounds;
            GD.Print($"BATTLE_BOUNDS fighter={model.FighterId} raw_pos={rawBounds.Position} raw_size={rawBounds.Size} visible_pos={bounds.Position} visible_size={bounds.Size} meshes={visibleMeshes}/{rawMeshes} excluded={excludedMeshes}");
            var top = _camera.UnprojectPosition(new Vector3(bounds.GetCenter().X, bounds.End.Y, bounds.GetCenter().Z));
            var foot = _camera.UnprojectPosition(new Vector3(bounds.GetCenter().X, bounds.Position.Y, bounds.GetCenter().Z));
            if (!(top.Y < foot.Y))
                throw new InvalidOperationException($"fighter {model.FighterId} projects head below feet headY={top.Y:0.###} footY={foot.Y:0.###}");
            // Screen-space visibility is asserted by the captured ordering-table image. The
            // hidden MeshInstance3D carriers use a different projection from that renderer.
            summaries.Add($"fighter={model.FighterId} headY={top.Y:0.###} footY={foot.Y:0.###}");
        }
        var stage = WorldBounds(_stage.Root, includeInvisible: true, out _, out _);
        var lowestFighter = _models.Min(model => WorldBounds(model.Root, includeInvisible: true, out _, out _).Position.Y);
        if (stage.Position.Y > lowestFighter)
            throw new InvalidOperationException($"stage floor is above fighter feet stageMinY={stage.Position.Y:0.###} fighterMinY={lowestFighter:0.###}");
        return string.Join(" ", summaries) + $" stageMinY={stage.Position.Y:0.###} fighterMinY={lowestFighter:0.###}";
    }

    private static Aabb WorldBounds(Node3D root) => WorldBounds(root, includeInvisible: false, out _, out _);

    private static Aabb WorldBounds(Node3D root, bool includeInvisible, out int includedMeshes, out int excludedMeshes)
    {
        Aabb? result = null;
        includedMeshes = 0;
        excludedMeshes = 0;
        foreach (var child in Descendants(root))
        {
            if (child is not MeshInstance3D mesh || mesh.Mesh is null) continue;
            // MeshInstance3D.Visible is local. IsVisibleInTree includes its bone's source-scale
            // visibility, which is the condition the decoded mesh contract uses before drawing the mesh.
            if (!includeInvisible && !mesh.IsVisibleInTree())
            {
                excludedMeshes++;
                continue;
            }
            var bounds = mesh.GlobalTransform * mesh.GetAabb();
            result = result is null ? bounds : result.Value.Merge(bounds);
            includedMeshes++;
        }
        return result ?? throw new InvalidOperationException($"node {root.Name} has no {(includeInvisible ? "mesh" : "visible mesh")} bounds");
    }

    private static IEnumerable<Node> Descendants(Node node)
    {
        foreach (Node child in node.GetChildren())
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static IEnumerable<Vector3> Corners(Aabb bounds)
    {
        for (var x = 0; x <= 1; x++)
            for (var y = 0; y <= 1; y++)
                for (var z = 0; z <= 1; z++)
                    yield return bounds.Position + new Vector3(x * bounds.Size.X, y * bounds.Size.Y, z * bounds.Size.Z);
    }

    /// <summary>Sets the white-flash overlay level (0 = off, 1 = full white).</summary>
    public void SetFlash(float level)
    {
        _flash.Color = new Color(1, 1, 1, Mathf.Clamp(level, 0f, 1f));
    }

    /// <summary>Shows or hides the hit spark on the struck fighter.</summary>
    public void ShowHitSpark(bool onEnemy, bool show)
    {
        var model = onEnemy ? (_models.Count > 1 ? _models[1] : null) : (_models.Count > 0 ? _models[0] : null);
        if (model is not null) _hitSpark.Position = WorldBounds(model.Root).GetCenter();
        _hitSpark.Visible = show;
    }

    private void ClearModels()
    {
        _modelLayer.Clear();
        _partnerModelLayer.Clear();
        _lastOrderingLog = null;
        _probeHiddenFighters.Clear();
        foreach (var model in _models) model.Root.QueueFree();
        _models.Clear();
        _partnerModel?.Root.QueueFree();
        _partnerModel = null;
        _partnerScreen.Visible = false;
        foreach (var (instance, _, _) in _placeholders) instance.QueueFree();
        _placeholders.Clear();
        _built = false;
    }

    public void Present(BattleView? view)
    {
        // Advance and apply the battle camera's interpolation each presented frame.
        StepCamera(1f / 60f);
        StepEntranceFade(1f / 60f);
        if (view is not { } value)
        {
            _hud.SetView(null);
            _partnerScreen.Visible = false;
            return;
        }
        if (!_probePoseOverride) StepMotions(1f / 60f);
        if (value.Phase == BattleFlowPhase.PlayerCommand) StepShotCamera();
        else StopShotCamera();
        // the player-turn presentation flow creates PartnerView for the entire PlayerCommand task, including its
        // submenus. It is killed when the task returns an action, not on submenu transitions.
        _partnerScreen.Visible = value.Phase == BattleFlowPhase.PlayerCommand && _partnerModel is not null;
        if (_partnerScreen.Visible) SetPartnerCamera();
        _modelLayer.Rebuild(_camera, _models);
        if (_partnerModel is not null) _partnerModelLayer.Rebuild(_partnerCamera, [_partnerModel]);
        else _partnerModelLayer.Clear();
        (int Polygons, int Culled, int AbrUnsorted) ordering = (_modelLayer.PolygonCount, _modelLayer.CulledCount, _modelLayer.AbrUnsortedCount);
        if (_lastOrderingLog != ordering)
        {
            _lastOrderingLog = ordering;
            GD.Print($"battle-ot polygons={ordering.Polygons} culled={ordering.Culled} abr-unsorted={ordering.AbrUnsorted}");
        }
        _hud.SetView(value);
    }

    /// <summary>Draws the whole battle HUD from the source layout (the HUD layout contract / command_the battle presentation flow): the
    /// HP bars top-left (enemy) and top-right (player) with the source colours, the HP numbers,
    /// the six-command menu on the left, the action message and a minimal terminal report.</summary>
    private sealed partial class BattleHudNode : Control
    {
        // the HUD layout contract FIGHTSTG_hpBars (EU): source 320x240 pixels, transformed once by the node.
        private static readonly Vector2[] PlayerBar = [new(0xAF, 0x25), new(0x12F, 0x25), new(0xAF, 0x2B), new(0x12F, 0x2B)];
        private static readonly Vector2[] EnemyBar = [new(0x8F, 0x25), new(0x0F, 0x25), new(0x8F, 0x2B), new(0x0F, 0x2B)];
        // the HUD layout contract FIGHTSTG_hpBarColors: {0} green dark, {1} green light (above a quarter HP),
        // {2} red dark, {3} red light (at or below a quarter).
        private static readonly Color GreenDark = new(0.0f, 0.44f, 0.16f);
        private static readonly Color GreenLight = new(0.0f, 0.78f, 0.24f);
        private static readonly Color RedDark = new(0.70f, 0.22f, 0.09f);
        private static readonly Color RedLight = new(1.0f, 0.22f, 0.05f);
        // command_the battle presentation flow: six lines at x 0x24, y 0x6D + i*0x13 (left column, downward).
        private const float MenuX = 0x24;
        private const float MenuY0 = 0x6D;
        private const float MenuLineStep = 0x13;

        private BattleView? _view;
        private FontData? _font;
        // Indexed by (FONT_STYLE, PALETTE_0_TO_7, atlas page). Each texture is
        // already recoloured by its exact PSX CLUT row, never Godot-tinted.
        private readonly PsxTextPainter _textPainter;
        private BattleHudData? _hud;
        private readonly Dictionary<int, ImageTexture[]> _frameTextures = [];
        private readonly Dictionary<int, Vector2> _frameOffsets = [];
        private ImageTexture? _cursorTexture;
        private ImageTexture? _cursorOffTexture;
        private BattleMenuIconsData? _menuIcons;
        private readonly Dictionary<int, ImageTexture[]> _menuIconTextures = [];
        private int _previousCursor = -1;
        private ulong? _probeAnimationMilliseconds;
        private ulong AnimationMilliseconds => _probeAnimationMilliseconds ?? Time.GetTicksMsec();

        public BattleHudNode()
        {
            SetAnchorsPreset(LayoutPreset.FullRect);
            MouseFilter = MouseFilterEnum.Ignore;
            _textPainter = new PsxTextPainter(this);
        }

        public void SetFont(FontData font, Texture2D page0, Texture2D page1)
        {
            _ = page0;
            _ = page1;
            _font = font;
            _textPainter.SetFont(font);
            QueueRedraw();
        }

        /// <summary>Binds the decoded battle-HUD frames (HP-bar caps, background, technique
        /// gauge, cursor) so _Draw renders the real disc sprites. A missing frame is logged and
        /// skipped (the flat panel remains the fallback), never silently drawn as a sprite.</summary>
        public void SetHud(BattleHudData? hud)
        {
            _hud = hud;
            _frameTextures.Clear();
            _frameOffsets.Clear();
            _cursorTexture = null;
            _cursorOffTexture = null;
            if (hud is null) { QueueRedraw(); return; }
            foreach (var (id, frame) in hud.Frames)
            {
                var rows = frame.ClutRows.Select(path => Image.LoadFromFile(path)).ToArray();
                if (rows.Any(image => image is null || image.IsEmpty())) { GD.PushWarning($"WARN battle-hud-frame-unreadable frame={id}"); continue; }
                _frameTextures[id] = [.. rows.Select(image => ImageTexture.CreateFromImage(image!))];
                _frameOffsets[id] = new Vector2(frame.OffsetX, frame.OffsetY);
            }
            var cursorImage = Image.LoadFromFile(hud.Cursor.Path);
            if (cursorImage is null || cursorImage.IsEmpty())
                GD.PushWarning($"WARN battle-hud-cursor-unreadable path={hud.Cursor.Path}");
            else
                _cursorTexture = ImageTexture.CreateFromImage(cursorImage);
            var cursorOffImage = Image.LoadFromFile(hud.Cursor.OffPath);
            if (cursorOffImage is null || cursorOffImage.IsEmpty())
                GD.PushWarning($"WARN battle-hud-cursor-off-unreadable path={hud.Cursor.OffPath}");
            else
                _cursorOffTexture = ImageTexture.CreateFromImage(cursorOffImage);
            QueueRedraw();
        }

        public void SetMenuIcons(BattleMenuIconsData icons)
        {
            _menuIcons = icons;
            _menuIconTextures.Clear();
            foreach (var (id, frame) in icons.Frames)
            {
                var images = frame.ClutRows.Select(Image.LoadFromFile).ToArray();
                if (images.Any(image => image is null || image.IsEmpty()))
                    throw new InvalidOperationException($"menu icon image unreadable frame={id}");
                _menuIconTextures[id] = [.. images.Select(image => ImageTexture.CreateFromImage(image!))];
            }
            QueueRedraw();
        }

        public void SetProbeAnimationMilliseconds(ulong? milliseconds)
        {
            _probeAnimationMilliseconds = milliseconds;
            QueueRedraw();
        }

        public void SetView(BattleView? view)
        {
            _view = view;
            QueueRedraw();
        }

        public string AssertLayout()
        {
            if (!BattleHudLayout.FitsSourceRect(8, 15, 151, 43)
                || !BattleHudLayout.FitsSourceRect(161, 15, 151, 43)
                || !BattleHudLayout.FitsSourceRect(11, 188, 298, 40)
                || !BattleHudLayout.FitsSourceRect(34, 16, 12, 12))
                throw new InvalidOperationException("battle HUD source bounds exceed 320x240");
            var cursor = BattleHudLayout.ToViewportSize(12, 12);
            if (cursor != (36, 36)) throw new InvalidOperationException($"menu cursor viewport size={cursor.Width}x{cursor.Height}, expected=36x36");
            return $"hud=320x240 scale={BattleHudLayout.Scale} offset=({BattleHudLayout.OffsetX},{BattleHudLayout.OffsetY}) cursor=12x12->36x36 bounds=inside";
        }

        public override void _Draw()
        {
            if (_view is not { } view) return;
            DrawHudFrames(view);
            DrawHpBar(view, side: 0, view.Players);
            DrawHpBar(view, side: 1, view.Enemies);
            DrawFighterNames(view);
            DrawTeamIcons(view);
            DrawHpNumbers(view);
            DrawMessage(view);
            if (view.Phase == BattleFlowPhase.PlayerCommand)
                DrawCommandMenu(view);

        }

        /// <summary>The decoded sheet sprites FIGHTSTG_drawHud draws: the two HP-bar window
        /// frames (0 at (8, 0xF), 1 at (0xA1, 0xF)), the background (0xB, 0x1E) and the
        /// technique gauge (0x33 at (0x109, 0x3E)), each at the source position scaled 4x.</summary>
        private void DrawHudFrames(BattleView view)
        {
            DrawFrame(0, 8, 0xF);
            DrawFrame(1, 0xA1, 0xF);
            DrawFrame(0x1E, 0x104, 0x3D);
            var player = view.Players.FirstOrDefault(fighter => fighter.Active);
            var gaugeFrame = player.MaxMp > 0 && player.Mp >= player.MaxMp ? 0x37 : 0x33;
            DrawFrame(gaugeFrame + (int)((AnimationMilliseconds / 16 / 4) & 3), 0x109, 0x3E);
            DrawFrame(0xB, 0x104, 0x3D);
            // the auxiliary fighter-view contract: FILE_BATTLE_MENU/WFIGHTMN frame 10 at (246,74), on the menu
            // layer so it surrounds the dedicated 3D layer beneath this HUD.
            if (view.Phase == BattleFlowPhase.PlayerCommand) DrawFrame(10, 246, 74);
        }

        private void DrawFrame(int frameId, int x, int y, int clutRow = 0, Color? modulate = null)
        {
            if (!_frameTextures.TryGetValue(frameId, out var rows)) return;
            // SpriteDrawer.draw places every part at (x + dx, y + dy). The decoder crops the
            // composed frame and records that crop origin so source anchors remain exact.
            var offset = _frameOffsets.TryGetValue(frameId, out var value) ? value : Vector2.Zero;
            DrawTexture(rows[Math.Clamp(clutRow, 0, rows.Length - 1)], new Vector2(x, y) + offset, modulate ?? Colors.White);
        }

        private void DrawMenuIcon(int frameId, int x, int y, int clutRow = 0)
        {
            if (_menuIcons is null || !_menuIcons.Frames.TryGetValue(frameId, out var frame)
                || !_menuIconTextures.TryGetValue(frameId, out var rows)) return;
            DrawTexture(rows[Math.Clamp(clutRow, 0, rows.Length - 1)], new Vector2(x + frame.OffsetX, y + frame.OffsetY));
        }

        /// <summary>The HP bar of one side, at the source position, shrinking toward the source
        /// end (EU: the player's shrinks left, the enemy's right) and coloured green above a
        /// quarter HP and red at or below it.</summary>
        private void DrawHpBar(BattleView view, int side, IReadOnlyList<BattleFighterView> fighters)
        {
            _ = view;
            var active = fighters.FirstOrDefault(fighter => fighter.Active);
            if (active.Id == 0) return;
            var points = side == 0 ? PlayerBar : EnemyBar;
            var left = points[0];
            var right = points[1];
            var top = points[0].Y;
            var bottom = points[2].Y;
            var width = right.X - left.X;
            var ratio = active.MaxHp > 0 ? Mathf.Clamp((float)active.Hp / active.MaxHp, 0f, 1f) : 0f;
            var (dark, light) = active.Hp > active.MaxHp / 4 ? (GreenDark, GreenLight) : (RedDark, RedLight);
            // Side 0 (player) shrinks toward its left end; side 1 (enemy) toward its right end.
            var filled = width * ratio;
            var rect = side == 0
                ? new Rect2(new Vector2(right.X - filled, top), new Vector2(filled, bottom - top))
                : new Rect2(new Vector2(left.X, top), new Vector2(filled, bottom - top));
            DrawRect(rect, light);
            // The darker under-tone: the source is a two-colour gradient quad; the fill's dark
            // end is approximated by a thin darker strip at the low end.
            var stripWidth = Mathf.Min(filled, 6f);
            var stripRect = side == 0
                ? new Rect2(new Vector2(right.X - stripWidth, top), new Vector2(stripWidth, bottom - top))
                : new Rect2(new Vector2(left.X, top), new Vector2(stripWidth, bottom - top));
            DrawRect(stripRect, dark);
        }

        private void DrawHpNumbers(BattleView view)
        {
            var player = view.Players.FirstOrDefault(fighter => fighter.Active);
            if (player.Id != 0)
            {
                // the HUD layout contract: current at 0x10B, max at 0x12E. Both are style-3 text windows.
                DrawTextWindow(3, 0x10B, 0x1A, $"{player.Hp}", rightAlign: true, palette: PaletteWhite);
                DrawTextWindow(3, 0x12E, 0x1A, $"{player.MaxHp}", rightAlign: true, palette: PaletteWhite);
            }
        }


        private void DrawFighterNames(BattleView view)
        {
            var player = view.Players.FirstOrDefault(fighter => fighter.Active);
            var enemy = view.Enemies.FirstOrDefault(fighter => fighter.Active);
            if (player.Id != 0) DrawTextWindow(1, 0xAE, 0x15, player.Name, string.Empty, rightAlign: false, palette: PaletteWhite);
            if (enemy.Id != 0) DrawTextWindow(1, 0x11, 0x15, enemy.Name, string.Empty, rightAlign: false, palette: PaletteWhite);
        }

        private void DrawTeamIcons(BattleView view)
        {
            int[][] iconX = [[0x10F, 0x11A, 0x125], [0x25, 0x1A, 0x0F]];
            var sides = new[] { view.Players, view.Enemies };
            for (var side = 0; side < sides.Length; side++)
                for (var slot = 0; slot < Math.Min(3, sides[side].Count); slot++)
                {
                    var fighter = sides[side][slot];
                    if (fighter.Id == 0) continue;
                    var baseFrame = fighter.KnockedOut ? 2 : fighter.Hp != fighter.MaxHp ? 1 : 0;
                    DrawFrame(baseFrame + (fighter.Active ? 0xC : 0x3C), iconX[side][slot], 0x30);
                }
        }

        /// <summary>The six-command menu at the source left column, with the cursor highlight.</summary>
        private void DrawCommandMenu(BattleView view)
        {
            if (view.Menu is not { } menu) return;
            var labels = menu.Lines.Select(line => line.Label).ToArray();
            if (menu.Menu != BattleMenuKind.Root)
            {
                switch (menu.Menu)
                {
                    case BattleMenuKind.Switch:
                        DrawSwitchMenu(view, menu);
                        return;
                    case BattleMenuKind.SwitchIn:
                    case BattleMenuKind.PairSwitch:
                        DrawSwitchInMenu(menu);
                        return;
                    case BattleMenuKind.Technique:
                        DrawTechniqueMenu(view, menu);
                        return;
                    case BattleMenuKind.Digivolve:
                        DrawDigivolveMenu(menu);
                        return;
                    case BattleMenuKind.Item:
                        DrawItemMenu(menu);
                        return;
                    default:
                        throw new InvalidOperationException($"unknown battle menu {menu.Menu}");
                }
            }
            var cursorFrame = (int)((AnimationMilliseconds / 16) % 12);
            for (var i = 0; i < labels.Length; i++)
            {
                var origin = new Vector2(MenuX, MenuY0 + i * MenuLineStep);
                var blinkRow = i == menu.Cursor ? (int)((AnimationMilliseconds / 16 / 4) % 5) : 0;
                DrawFrame(0x22, 16, 109 + i * 19, blinkRow);
                var palette = menu.Lines[i].Enabled ? PaletteWhite : PaletteGrey;
                DrawBattleText(origin, menu.Lines[i].Text, string.Empty, palette);
            }
            if (_previousCursor >= 0 && _previousCursor != menu.Cursor && _cursorOffTexture is { } off)
                DrawTexture(off, new Vector2(17, 110 + _previousCursor * 19));
            if (_cursorTexture is { } cursor)
                DrawTextureRectRegion(cursor, new Rect2(17, 110 + menu.Cursor * 19, 12, 12), new Rect2(cursorFrame * 12, 0, 12, 12));
            _previousCursor = menu.Cursor;
        }

        private void DrawTechniqueMenu(BattleView view, BattleMenuView menu)
        {
            // tech_the battle presentation flow:24-39,50-81. The list is six lines, source cursor has no line sprite.
            DrawFrame(0x2A, 8, 0x3E);
            DrawFrame(0x29, 0xA3, 0x21);
            DrawFrame(0x31, 0xB, 0xBC);
            DrawPageArrows(menu, 0x9F);
            DrawTextWindow(3, 0x1A, 0x9F, menu.PreviousPageText, string.Empty, rightAlign: false, palette: PaletteDarkBlue);
            DrawTextWindow(3, 0x80, 0x9F, menu.NextPageText, string.Empty, rightAlign: false, palette: PaletteDarkBlue);
            var player = view.Players.FirstOrDefault(fighter => fighter.Active);
            DrawTextWindow(3, 0xAC, 0x3A, menu.StatusText?.Header, string.Empty, rightAlign: false, palette: PaletteWhite);
            if (menu.StatusText?.CurrentOverride is { } currentOverride)
                DrawTextWindow(3, 0xD8, 0x3A, currentOverride, string.Empty, rightAlign: true, palette: PaletteWhite);
            else
                DrawTextWindow(3, 0xD8, 0x3A, player.Mp.ToString(), rightAlign: true, palette: PaletteWhite);
            DrawTextWindow(3, 0xD9, 0x3A, menu.StatusText?.Separator, string.Empty, rightAlign: false, palette: PaletteWhite);
            DrawTextWindow(3, 0xFB, 0x3A, player.MaxMp.ToString(), rightAlign: true, palette: PaletteWhite);
            for (var i = 0; i < menu.Lines.Count && i < 6; i++)
            {
                var line = menu.Lines[i];
                DrawMenuIcon(line.Icon, 0x1D, 0x45 + i * 0xE);
                DrawTextWindow(1, 0x2A, 0x45 + i * 0xE, line.Text, line.Label, rightAlign: false,
                    line.Enabled ? PaletteWhite : PaletteGrey);
            }
            var selected = menu.Lines.Count == 0 ? null : menu.Lines[menu.Cursor];
            DrawTextWindow(1, 0x14, 0xC2, selected?.DescriptionText, selected?.Description ?? string.Empty, rightAlign: false, palette: selected?.Enabled == false ? PaletteGrey : PaletteWhite);
            DrawTextWindow(1, 0x100, 0xD0, selected?.CostLabelText, string.Empty, rightAlign: false, palette: selected?.Enabled == false ? PaletteGrey : PaletteWhite);
            if (selected is not null && selected.Cost >= 0)
                DrawTextWindow(1, 0x12B, 0xD0, selected.Cost.ToString(), rightAlign: true, palette: selected.Enabled ? PaletteWhite : PaletteGrey);
            DrawCursor(14, 69 + menu.Cursor * 14);
        }

        private void DrawItemMenu(BattleMenuView menu)
        {
            // item_the battle presentation flow:22-37,45-59. The list is seven lines, source cursor has no line sprite.
            DrawFrame(0x27, 8, 0x3E);
            DrawFrame(0x28, 0xA6, 0xA0);
            DrawFrame(0x31, 0xB, 0xBC);
            DrawPageArrows(menu, 0xA9);
            DrawTextWindow(3, 0x1A, 0xA9, menu.PreviousPageText, string.Empty, rightAlign: false, palette: PaletteDarkBlue);
            DrawTextWindow(3, 0x80, 0xA9, menu.NextPageText, string.Empty, rightAlign: false, palette: PaletteDarkBlue);
            for (var i = 0; i < menu.Lines.Count && i < 7; i++)
            {
                var line = menu.Lines[i];
                DrawMenuIcon(line.Icon, 0x1D, 0x45 + i * 0xE);
                DrawTextWindow(1, 0x2A, 0x45 + i * 0xE, line.Text, line.Label, rightAlign: false,
                    line.Enabled ? PaletteWhite : PaletteGrey);
            }
            var selected = menu.Lines.Count == 0 ? null : menu.Lines[menu.Cursor];
            DrawTextWindow(1, 0x14, 0xC2, selected?.DescriptionText, selected?.Description ?? string.Empty, rightAlign: false, palette: PaletteWhite);
            DrawTextWindow(1, 0xAC, 0xA5, menu.StatusText?.Header, string.Empty, rightAlign: false, palette: PaletteWhite);
            if (selected is not null && selected.Quantity >= 0)
                DrawTextWindow(1, 0xC6, 0xA5, selected.Quantity.ToString(), rightAlign: true, palette: PaletteWhite);
            DrawCursor(14, 69 + menu.Cursor * 14);
        }

        /// <summary>tech_the battle presentation flow/item_the battle presentation flow: blink WFIGHTMN 0x1F/0x20 when the page exists.</summary>
        private void DrawPageArrows(BattleMenuView menu, int y)
        {
            if ((AnimationMilliseconds & 0x10) == 0) return;
            if (menu.First > 0) DrawFrame(0x1F, 0x10, y);
            if (menu.First / menu.PageSize + 1 < menu.PageCount) DrawFrame(0x20, 0x90, y);
        }

        private void DrawDigivolveMenu(BattleMenuView menu)
        {
            // digivolve_the battle presentation flow:35,51-53. Frame 0x2B holds the right-side partner pane.
            DrawFrame(0x2B, 0xA7, 0x8F);
            for (var i = 0; i < menu.Lines.Count; i++)
            {
                var line = menu.Lines[i];
                DrawFrame(0x24, 167, 146 + i * 19);
                DrawTextWindow(1, 0xBB, 0x92 + i * 0x13, line.Text, line.Label, rightAlign: false,
                    line.Enabled ? PaletteWhite : PaletteGrey);
            }
            DrawCursor(168, 147 + menu.Cursor * 19);
        }

        private void DrawCursor(int x, int y)
        {
            if (_cursorTexture is not { } cursor) return;
            var frame = (int)((AnimationMilliseconds / 16) % 12);
            DrawTextureRectRegion(cursor, new Rect2(x, y, 12, 12), new Rect2(frame * 12, 0, 12, 12));
        }

        private void DrawSwitchMenu(BattleView view, BattleMenuView menu)
        {
            // switch_the battle presentation flow:39-44,51-95. Source coordinates stay in the 320x240 HUD space.
            var candidates = view.Players.Where(fighter => !fighter.Active && fighter.Id != 0).ToArray();
            var cursorFrame = (int)((AnimationMilliseconds / 16) % 12);
            for (var i = 0; i < menu.Lines.Count && i < candidates.Length; i++)
            {
                var fighter = candidates[i];
                var y = 0x4E + i * 0x20;
                var palette = fighter.KnockedOut ? PaletteGrey : PaletteWhite;
                // FIGHTSTG_switchCursor (item_the battle presentation flow:98-100): sprite 0x2F is the
                // opaque cyan-bordered backing of every candidate line.  The selected
                // line cycles its five source CLUT rows in cursor animation:50-82.
                var blinkRow = i == menu.Cursor ? (int)((AnimationMilliseconds / 16 / 4) % 5) : 0;
                DrawFrame(0x2F, 16, 77 + i * 32, blinkRow);
                DrawTextWindow(1, 0x24, 0x4C + i * 0x20, menu.Lines[i].Text, menu.Lines[i].Label, rightAlign: false, palette);
                DrawTextWindow(3, 0x6C, y, "HP", rightAlign: false, palette: PaletteWhite);
                DrawTextWindow(3, 0x98, y, fighter.Hp.ToString(), rightAlign: true, palette: PaletteWhite);
                DrawTextWindow(3, 0xBB, y, fighter.MaxHp.ToString(), rightAlign: true, palette: PaletteWhite);
                DrawTextWindow(3, 0x6C, y + 0xE, "MP", rightAlign: false, palette: PaletteWhite);
                DrawTextWindow(3, 0x98, y + 0xE, fighter.Mp.ToString(), rightAlign: true, palette: PaletteWhite);
                DrawTextWindow(3, 0xBB, y + 0xE, fighter.MaxMp.ToString(), rightAlign: true, palette: PaletteWhite);
                // pair icon 0x30 is intentionally absent until getPairDigimon has recovered a
                // real pair-tech relation for this exact candidate.
            }
            if (_cursorTexture is { } cursor)
                DrawTextureRectRegion(cursor, new Rect2(17, 78 + menu.Cursor * 32, 12, 12), new Rect2(cursorFrame * 12, 0, 12, 12));
        }

        private void DrawSwitchInMenu(BattleMenuView menu)
        {
            // switch_in_the battle presentation flow:15-19,27-36,117-125. Frame 0x2B is the incoming-form pane;
            // 0x29 and the choice cursor are only present after a form is selected.
            DrawFrame(0x2B, 0xA7, 0x8F);
            if (menu.Menu == BattleMenuKind.PairSwitch)
            {
                // The reconstructed state has no pair technique. Source draws 0x29 (and
                // frame 2) only when findPairTech set task->tech, so do not invent that panel.
                DrawTextWindow(1, 0xAB, 0x92, menu.Lines[0].Text, menu.Lines[0].Label, rightAlign: false, palette: PaletteWhite);
                DrawTextWindow(1, 0xBB, 0xA5, menu.Lines[0].Text, menu.Lines[0].Label, rightAlign: false, palette: PaletteWhite);
                var choiceBlinkRow = (int)((AnimationMilliseconds / 16 / 4) % 5);
                for (var i = 0; i < menu.Lines.Count && i < 2; i++)
                    DrawFrame(0x22, 167, 165 + i * 19, i == menu.Cursor ? choiceBlinkRow : 0);
                if (_cursorTexture is { } cursor)
                    DrawTextureRectRegion(cursor, new Rect2(168, 166 + menu.Cursor * 19, 12, 12), new Rect2(((int)(AnimationMilliseconds / 16) % 12) * 12, 0, 12, 12));
                return;
            }
            var formBlinkRow = (int)((AnimationMilliseconds / 16 / 4) % 5);
            for (var i = 0; i < menu.Lines.Count; i++)
            {
                // FIGHTSTG_pairCursors[0]: sprite 0x24 backs each incoming-form line.
                DrawFrame(0x24, 167, 146 + i * 19, i == menu.Cursor ? formBlinkRow : 0);
                DrawTextWindow(1, 0xBB, 0x92 + i * 0x13, menu.Lines[i].Text, menu.Lines[i].Label, rightAlign: false, palette: PaletteWhite);
            }
            if (_cursorTexture is { } formCursor)
                DrawTextureRectRegion(formCursor, new Rect2(168, 147 + menu.Cursor * 19, 12, 12), new Rect2(((int)(AnimationMilliseconds / 16) % 12) * 12, 0, 12, 12));
        }

        private void DrawMessage(BattleView view)
        {
            if (!view.MessageVisible) return;
            // the battle-message layout contract: WFIGHTMN frame 0x31 at (0xB,0xBC), lines at (0x14,0xC2)/(0x14,0xD0).
            DrawFrame(0x31, 0xB, 0xBC);
            DrawTextWindow(1, 0x14, 0xC2, view.MessageText, view.Message, rightAlign: false, palette: PaletteWhite);
            if (view.MessageText?.Second is { } second)
                DrawTextWindow(1, 0x14, 0xD0, second, second.Debug, rightAlign: false, palette: PaletteWhite);
            // the battle-message layout contract: arrowPalette increments every four source frames and wraps at five.
            if (_menuIcons is { } icons)
                DrawMenuIcon(icons.ArrowFrame, icons.ArrowX, icons.ArrowY,
                    (int)(AnimationMilliseconds / 16UL / (ulong)icons.ArrowTimeStep) % 5);
        }


        private static int ToSourceGlyphCode(char character) => character switch
        {
            ' ' => 1, // the text-token layout contract: Shift-JIS 0x8140
            >= '0' and <= '9' => 4 + character - '0', // 0x824F..0x8258
            >= 'A' and <= 'Z' => 14 + character - 'A', // 0x8260..0x8279
            >= 'a' and <= 'z' => 40 + character - 'a', // 0x8281..0x829A
            _ => -1, // punctuation needs an identified source text code, never guessed
        };

        private const int PaletteWhite = 0;
        private const int PaletteDarkBlue = 2;
        private const int PaletteYellow = 3;
        private const int PaletteGreen = 4;
        private const int PaletteGrey = 7;
        private readonly HashSet<string> _unsourcedTextWarnings = [];
        private void WarnUnsourced(string key) { if (_unsourcedTextWarnings.Add(key)) GD.PushWarning($"WARN battle-text-unsourced key={key}"); }

        /// <summary>PSX text-window semantics. Palette is text.h's row offset
        /// 0..7, selecting an exact decoded atlas CLUT instead of a Godot tint.</summary>
        private void DrawTextWindow(int style, int x, int y, string text, bool rightAlign, int palette)
            => _textPainter.DrawWindow(style, x, y, text, rightAlign, palette);

        private void DrawTextWindow(int style, int x, int y, BattleTextLine? line, string fallback, bool rightAlign, int palette)
        {
            if (line is null || line == BattleTextLine.Missing) { WarnUnsourced(fallback); return; }
            _textPainter.DrawLine(style, x, y, line, rightAlign, palette);
        }


        private void DrawBattleText(Vector2 origin, BattleTextLine? line, string fallback, int palette)
        {
            if (line is null || line == BattleTextLine.Missing) { WarnUnsourced(fallback); return; }
            _textPainter.DrawLine(1, (int)origin.X, (int)origin.Y, line, false, palette);
        }
    }
}
