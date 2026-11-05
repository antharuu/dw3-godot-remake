using Dw3Production.Conversion;
using Dw3Production.Simulation;
using Godot;

namespace Dw3Production.Presentation;

/// <summary>
/// The battle presentation: a full-screen 3D sub-viewport (the decoded fight stage, the EXP-300
/// battle models over it, a battle camera that focuses the active fighter) plus a 2D HUD overlay
/// (disc-derived HP bars and command menu). It is read-only over the flow controller, exactly
/// like DialogueRenderer over the dialogue state.
/// </summary>
public sealed class BattleRenderer : IDisposable
{
    private const int ViewportWidth = 1280;

    private const int ViewportHeight = 720;

    private const float PresentDelta = 1f / 60f;

    private readonly CanvasLayer _layer;

    private readonly Node3D _world;

    private readonly Camera3D _camera;

    private readonly BattlePartnerView _partnerView;

    private readonly WorldEnvironment _environment;

    private readonly BattleHudNode _hud;

    private readonly ColorRect _flash;

    private readonly Sprite3D _hitSpark;

    private readonly MeshInstance3D _floor;

    private readonly BattleFighterRoster _fighters = new();

    private readonly BattleModelLayer _modelLayer;

    private readonly List<MeshInstance3D> _placeholders = [];

    private readonly BattlePalClock _motionClock = new();

    private readonly BattleEntranceFade _entranceFade = new();

    private readonly BattleCameraFade _cameraFade = new();

    private readonly BattleCommandCamera _commandCamera = new();

    private readonly BattleSourceClock _shotClock = new();

    private (int Polygons, int Culled, int UnsupportedAbr)? _lastOrderingLog;

    private BattleModel? _stage;

    private string _lastModelLoadFailure = "unknown model loader failure";

    private bool _probePoseOverride;

    private bool _hudVisibleForProbe = true;

    private BattleModel.BattleLightSet? _stageLights;

    /// <summary>
    /// Initializes a new instance of the <see cref="BattleRenderer"/> class, hidden, under
    /// <paramref name="root"/>.
    /// </summary>
    /// <param name="root">Node that receives the battle canvas layer.</param>
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
        _camera = CreateCamera();
        _world.AddChild(_camera);
        _environment = CreateEnvironment();
        _world.AddChild(_environment);
        _floor = CreateFloor();
        _world.AddChild(_floor);
        _modelLayer = new BattleModelLayer(_layer, "BattleModelOrderingLayer");
        _partnerView = new BattlePartnerView(_layer);

        var screen = new SubViewportContainer { Stretch = true };
        screen.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        screen.AddChild(viewport);
        _layer.AddChild(screen);
        _modelLayer.MoveAfter(screen);

        // PS1 320x240 -> 1280x720 at exact 3x with 160px side margins, matching dialogue.
        _hud = new BattleHudNode
        {
            Scale = new Vector2(BattleHudLayout.Scale, BattleHudLayout.Scale),
            Position = new Vector2(BattleHudLayout.OffsetX, BattleHudLayout.OffsetY),
        };
        _layer.AddChild(_hud);

        // The white-flash overlay: a full-screen quad on the front layer.
        _flash = new ColorRect { Color = new Color(1, 1, 1, 0), MouseFilter = Control.MouseFilterEnum.Ignore };
        _flash.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _layer.AddChild(_flash);
        _partnerView.MoveScreenToFront();
        _hitSpark = CreateHitSpark();
        _world.AddChild(_hitSpark);
    }

    /// <summary>Gets current source stage fade (0..1), shared by lights, stage colour and background.</summary>
    public float LightFade => _entranceFade.Fraction;

    /// <summary>Gets a value indicating whether true once at least one battle model is built and the scene is showing.</summary>
    public bool Built { get; private set; }

    /// <summary>Gets a value indicating whether the battle canvas is currently visible.</summary>
    public bool Visible => _layer.Visible;

    /// <summary>Gets a value indicating whether true once a decoded fight stage is loaded and drawn as the background.</summary>
    public bool StageBuilt => _stage is not null;

    /// <summary>Gets the number of real battle models built (player + any decoded enemy).</summary>
    public int ModelCount => _fighters.Models.Count;

    /// <summary>Gets the unmodified main source camera for diagnostic actor translation.</summary>
    internal Camera3D CameraForProbe => _camera;

    /// <summary>Gets the actual command executor for controlled offscreen evidence.</summary>
    internal BattleModelLayer RasterForProbe => _modelLayer;

    /// <summary>Gets the actual partner render target for controlled offscreen evidence.</summary>
    internal BattlePartnerView PartnerForProbe => _partnerView;

    /// <summary>Gets a value indicating whether the HUD is visible in the composed production canvas.</summary>
    internal bool EffectiveHudVisibleForProbe => _hud.Visible;

    /// <summary>Maps a deterministic HUD frame to its source animation milliseconds.</summary>
    /// <param name="frame">Non-negative frame number to convert.</param>
    /// <returns>The source-clock time represented by <paramref name="frame"/>.</returns>
    public static ulong ProbeAnimationMilliseconds(int frame) => (ulong)ValidateProbeAnimationFrame(frame) * 16UL;

    /// <summary>Probe oracle for the source HUD mapping and command cursor bounds.</summary>
    /// <returns>A description of the verified HUD layout.</returns>
    public static string AssertHudLayout() => BattleHudNode.AssertLayout();

    /// <summary>Makes the battle canvas visible.</summary>
    public void Show() => _layer.Visible = true;

    /// <summary>Hides the battle canvas without releasing its loaded scene.</summary>
    public void Hide() => _layer.Visible = false;

    /// <summary>Probe-only HUD suppression for a model/stage-only framebuffer comparison.</summary>
    /// <param name="visible">Whether the HUD should be included in the capture.</param>
    public void SetHudVisibleForProbe(bool visible)
    {
        _hudVisibleForProbe = visible;
        ApplyEntranceOverlayVisibility();
    }

    /// <summary>Shows or hides the model layer's untextured canvas visibility control.</summary>
    /// <param name="visible">Whether the probe triangle is drawn.</param>
    public void SetModelLayerDiagnosticTriangleVisibleForProbe(bool visible) =>
        _modelLayer.SetProbeDiagnosticTriangleVisible(visible);

    /// <summary>Shows or hides the model layer's production Canvas calibration fixture.</summary>
    /// <param name="visible">Whether the synthetic Polygon2D fixture is drawn.</param>
    /// <param name="legacyDoubleUv">Whether to reproduce the pre-repair UV conversion for RED evidence.</param>
    public void SetModelLayerCanvasCalibrationVisibleForProbe(bool visible, bool legacyDoubleUv) =>
        _modelLayer.SetProbeCanvasCalibrationVisible(visible, legacyDoubleUv);

    /// <summary>Loads the decoded fight stage (the battle background) for the encounter's
    /// <paramref name="stageId"/>. Its first keyframe is posed during setup, then its recovered
    /// motion advances on the same PAL clock as the fighters. Returns false (and logs) when the
    /// JSON is absent or invalid; the dark floor then stays as the background, never silently
    /// presented as the real stage.</summary>
    /// <param name="stageJsonPath">Path to the extracted stage model document.</param>
    /// <param name="stageId">Identifier used to report a stage loading failure.</param>
    /// <returns><see langword="true"/> when the stage is ready to render, otherwise <see langword="false"/>.</returns>
    public bool BuildStage(string stageJsonPath, int stageId)
    {
        try
        {
            _stage?.Root.QueueFree();
            _stage = BattleModel.Load(_world, stageJsonPath);

            // The stage leaves the plain model control at identity. Model vertices, bones,
            // fighters and camera all share the same /256 PS1-to-Godot conversion.
            _stage.Root.Position = Vector3.Zero;
            _stage.Root.Scale = Vector3.One;
            _stageLights = _stage.StageLights ?? throw new InvalidOperationException("fight stage has no LightSet");
            RestartEntranceFade();

            // The stage is a plain model: one motion, no idle loop. Pose it at its motion's first
            // keyframe as a static arena.
            var keyframes = _stage.MotionKeyframes(0);
            _stage.Pose(keyframes.Count > 0 ? keyframes[0] : 0);
            _floor.Visible = false;
            return true;
        }
        catch (Exception error) when (IsModelLoadError(error))
        {
            GD.PushWarning($"WARN battle-stage-load-failed stage={stageId} reason={error.Message}");
            _stage = null;
            return false;
        }
    }

    /// <summary>Builds the active player's model on the left and the real enemy model on the right.
    /// The enemy model comes from the disc model decoder (same EXP-300 chain, parameterised per
    /// fighter); if its archive is not decodable a clearly distinct capsule stands in and is
    /// explicitly logged, never silently presented as the real model.</summary>
    /// <param name="playerModelJsonPath">Path to the active player's extracted model document.</param>
    /// <param name="enemyModelJsonPath">Path to the opponent's extracted model document.</param>
    /// <param name="playerLabel">Display label assigned to the player model node.</param>
    /// <param name="enemyLabel">Display label assigned to the opponent model node.</param>
    /// <param name="enemyFighterId">Identifier of the opponent represented by the model.</param>
    public void BuildModels(string playerModelJsonPath, string enemyModelJsonPath, string playerLabel, string enemyLabel, int enemyFighterId)
    {
        ClearModels();
        var playerFighterId = ParseFighterId(playerModelJsonPath);
        if (!TryAddModel(playerModelJsonPath, playerLabel, playerFighterId, enemy: false))
        {
            GD.PushWarning($"WARN battle-player-model-undecoded fighter={playerFighterId} capsule-stand-in label={playerLabel}");
            AddPlaceholder(playerFighterId, playerLabel, enemy: false, new Color(0.1f, 0.3f, 0.75f));
        }

        if (_partnerView.TryBuild(playerModelJsonPath, playerFighterId))
        {
            SynchronizePartnerPresentation();
            ApplyStageLights();
        }

        if (!TryAddModel(enemyModelJsonPath, enemyLabel, enemyFighterId, enemy: true))
        {
            // The enemy archive is not decodable (missing or malformed conversion): the capsule
            // is an explicit, logged fallback, never a silent stand-in.
            GD.PushWarning($"WARN battle-enemy-model-undecoded fighter={enemyFighterId} capsule-stand-in label={enemyLabel}");
            AddPlaceholder(enemyFighterId, enemyLabel, enemy: true, new Color(0.6f, 0.1f, 0.2f));
        }

        Built = true;
    }

    /// <summary>Builds exactly one source fighter for the offline contact sheet. It uses the same
    /// loader, idle pose, stage lighting and source camera as presentation, but removes the
    /// opponent and HUD so every tile remains an inspectable fighter render.</summary>
    /// <param name="modelJsonPath">Path to the extracted fighter model document.</param>
    /// <param name="label">Display label assigned to the fighter model node.</param>
    /// <param name="fighterId">Identifier of the fighter represented by the model.</param>
    /// <param name="enemy">Whether to place the fighter on the opponent side.</param>
    public void BuildSingleModelForSheet(string modelJsonPath, string label, int fighterId, bool enemy)
    {
        ClearModels();
        if (!TryAddModel(modelJsonPath, label, fighterId, enemy))
        {
            throw new InvalidOperationException($"sheet fighter={fighterId} model load failed: {_lastModelLoadFailure}");
        }

        Built = true;
        HideOverlaysForSheet();
    }

    /// <summary>Leaves only stage 18 for the contact-sheet background mask.</summary>
    public void ClearModelsForSheet()
    {
        ClearModels();
        HideOverlaysForSheet();
    }

    /// <summary>Opaque pixels in the dedicated 100x60 source render target. This is deliberately
    /// independent from the main framebuffer, whose stage lies behind the thumbnail rectangle.</summary>
    /// <returns>The number of opaque pixels in the partner view target.</returns>
    public int PartnerViewOpaquePixels() => _partnerView.OpaquePixels();

    /// <summary>Writes the raw 100x60 target for visual probe evidence.</summary>
    /// <param name="path">Destination path for the image file.</param>
    /// <returns>The engine result of saving the render target.</returns>
    public Error SavePartnerView(string path) => _partnerView.Save(path);

    /// <summary>Probe-only visibility toggle for the independent 0x1009 partner model.</summary>
    /// <param name="visible">Whether the partner model should be rendered.</param>
    public void SetPartnerModelVisibleForProbe(bool visible)
    {
        var model = _partnerView.Model ?? throw new InvalidOperationException("partner view model is not built");
        model.Root.Visible = visible;
    }

    /// <summary>Poses every built model at <paramref name="frame"/> (for the probe's distinct-pose proof).</summary>
    /// <param name="frame">Source pose frame applied to every built fighter.</param>
    public void PoseAll(int frame)
    {
        _fighters.PoseAll(frame);
        SynchronizePartnerPresentation();
    }

    /// <summary>Source scale-threshold status for an actual decoded model, used by the offscreen
    /// regression probe rather than inferred from its final pixels.</summary>
    /// <param name="fighterId">Identifier of the fighter to inspect.</param>
    /// <returns>A diagnostic string listing bones hidden by the scale threshold.</returns>
    public string BoneVisibility(int fighterId) =>
        $"fighter={fighterId} hidden=[{string.Join(",", _fighters.Require(fighterId).HiddenBones)}]";

    /// <summary>Probe: (opaque, translucent) surface counts for a fighter, or for the stage
    /// when <paramref name="fighterId"/> is negative.</summary>
    /// <param name="fighterId">Fighter identifier, or a negative value for the stage.</param>
    /// <returns>The counts of opaque and translucent surfaces.</returns>
    public (int Opaque, int Translucent) SurfacePasses(int fighterId)
    {
        var model = fighterId < 0 ? _stage : _fighters.Find(fighterId);
        if (model is null)
        {
            return (0, 0);
        }

        var opaque = model.Materials.Count(BattleModel.IsOpaqueMaterial);
        return (opaque, model.Materials.Count - opaque);
    }

    /// <summary>Whether an actual decoded bone survived the source scale threshold.</summary>
    /// <param name="fighterId">Identifier of the fighter that owns the bone.</param>
    /// <param name="bone">Zero-based bone index to inspect.</param>
    /// <returns><see langword="true"/> when the requested bone is visible.</returns>
    public bool BoneVisible(int fighterId, int bone) => _fighters.Find(fighterId)?.IsBoneVisible(bone) == true;

    /// <summary>Reports whether a bone still contributes at least one visible detached legacy surface.</summary>
    /// <param name="fighterId">Identifier of the decoded fighter.</param>
    /// <param name="bone">Source bone index to inspect.</param>
    /// <returns>True when the legacy render path composes a surface for this bone.</returns>
    public bool LegacySurfaceVisible(int fighterId, int bone) =>
        _fighters.Find(fighterId)?.HasVisibleLegacySurface(bone) == true;

    /// <summary>Counts locally visible primitives submitted by one bone to the common painter path.</summary>
    /// <param name="fighterId">Identifier of the decoded fighter.</param>
    /// <param name="bone">Source bone index to inspect.</param>
    /// <returns>Number of drawable source primitives.</returns>
    public int DrawablePolygonCount(int fighterId, int bone) =>
        _fighters.Find(fighterId)?.DrawablePolygonCount(bone) ?? 0;

    /// <summary>Reports the maximum matrix and scale difference against the pose oracle.</summary>
    /// <returns>The greatest difference, or positive infinity when no model is built.</returns>
    public float RotationOracleMaxDiff() => ModelCount == 0 ? float.PositiveInfinity : _fighters.Models.Max(model => model.RotationOracleMaxDiff());

    /// <summary>Screen-winding versus source-normal agreement for a decoded fighter.</summary>
    /// <param name="fighterId">Identifier of the fighter to evaluate.</param>
    /// <returns>The total sampled faces and the number whose winding agrees.</returns>
    public (int Total, int Agree) WindingAgreement(int fighterId) => _fighters.Require(fighterId).WindingAgreement(_camera);

    /// <summary>Describes the face-animation diagnostic state for a fighter.</summary>
    /// <param name="fighterId">Identifier of the fighter to inspect.</param>
    /// <returns>A textual summary of the face animation state.</returns>
    public string FaceSummary(int fighterId) => _fighters.Require(fighterId).FaceSummary();

    /// <summary>Counts face texture texels changed for a fighter.</summary>
    /// <param name="fighterId">Identifier of the fighter to inspect.</param>
    /// <returns>The changed texture-texel count.</returns>
    public int FaceChangedTexels(int fighterId) => _fighters.Require(fighterId).FaceChangedTexels();

    /// <summary>Counts changed texels in the independent partner-view copy of the active fighter.</summary>
    /// <returns>The changed destination texels, or zero when the view has no decoded model.</returns>
    public int PartnerFaceChangedTexels() => _partnerView.Model?.FaceChangedTexels() ?? 0;

    /// <summary>Supplies a deterministic random value to a fighter's probe-only face update.</summary>
    /// <param name="fighterId">Identifier of the fighter to update.</param>
    /// <param name="randomValue">Fixed random value returned during the update.</param>
    public void SetFaceRandomForProbe(int fighterId, int randomValue) => _fighters.Require(fighterId).SetFaceRandomForProbe(() => randomValue);

    /// <summary>Sets and advances a fighter's probe-only face blink timer.</summary>
    /// <param name="fighterId">Identifier of the fighter to update.</param>
    /// <param name="timer">Blink timer value to apply before stepping the fighter.</param>
    public void SetFaceBlinkTimerForProbe(int fighterId, int timer)
    {
        var model = _fighters.Require(fighterId);
        model.SetFaceBlinkTimerForProbe(timer);
        model.Step(0);
        SynchronizePartnerPresentation();
    }

    /// <summary>Probe-only baseline resembling the old identity-scale renderer. The next PoseAll
    /// restores source construction before any ordinary presentation continues.</summary>
    public void ShowAllBonesForProbe()
    {
        _fighters.ShowAllBonesForProbe();
        _probePoseOverride = true;
    }

    /// <summary>Probe-only visibility toggle for one decoded fighter in a framebuffer mask.</summary>
    /// <param name="fighterId">Identifier of the fighter whose visibility changes.</param>
    /// <param name="visible">Whether the fighter should be rendered.</param>
    public void SetModelVisibleForProbe(int fighterId, bool visible)
    {
        _fighters.SetVisibleForProbe(fighterId, visible);

        // CaptureBattle does not necessarily traverse Present while a probe freezes the pose.
        // Rebuild synchronously so the ordering mesh follows the probe's visibility mask.
        _modelLayer.Rebuild(_camera, _fighters.Models);
    }

    /// <summary>Holds a deterministic source frame across an offscreen capture. Ordinary battle
    /// presentation keeps its time-driven pose path because this is only called by a probe.</summary>
    /// <param name="frame">Source pose frame to hold during the capture.</param>
    public void SetProbePose(int frame)
    {
        PoseAll(frame);
        _probePoseOverride = true;
    }

    /// <summary>Pins HUD animation to a deterministic 16 ms source frame for an offscreen probe.</summary>
    /// <param name="frame">Frame to pin, or <see langword="null"/> to resume normal animation.</param>
    public void SetProbeAnimationFrame(int? frame) =>
        _hud.SetProbeAnimationMilliseconds(frame is { } pinned ? ProbeAnimationMilliseconds(pinned) : null);

    /// <summary>Probe-only culling baseline. Callers must restore Back before leaving the probe.</summary>
    /// <param name="mode">Cull mode to apply to every fighter surface.</param>
    public void SetCullModeForProbe(BaseMaterial3D.CullModeEnum mode) => _fighters.SetCullModeForProbe(mode);

    /// <summary>Poses every built model at motion <paramref name="motionIndex"/>'s keyframe
    /// <paramref name="frameInMotion"/> (for the probe's attack-pose proof).</summary>
    /// <param name="motionIndex">Zero-based motion index to pose.</param>
    /// <param name="frameInMotion">Frame within the selected motion.</param>
    public void PoseMotionAll(int motionIndex, int frameInMotion) => _fighters.PoseMotionAll(motionIndex, frameInMotion);

    /// <summary>Requests a one-based source motion for the active player model.</summary>
    /// <param name="motion">One-based motion number to select.</param>
    /// <param name="restart">Whether selection resets the motion to its first frame.</param>
    public void SetPlayerMotion(int motion, bool restart = true) => _fighters.Player?.SetMotion(motion, restart);

    /// <summary>Requests a recovered source motion only when the selected model carries it.
    /// A missing motion is surfaced to the host log rather than substituted with a guess.</summary>
    /// <param name="motion">One-based motion number to select.</param>
    /// <param name="restart">Whether selection resets the motion to its first frame.</param>
    /// <returns><see langword="true"/> when the active player has the requested motion.</returns>
    public bool TrySetPlayerMotion(int motion, bool restart = true)
    {
        if (_fighters.Player is not { } player || player.Motions.Count < motion)
        {
            return false;
        }

        player.SetMotion(motion, restart);
        return true;
    }

    /// <summary>Requests one-based source motion numbers for all real fighter models.</summary>
    /// <param name="motion">One-based motion number to select.</param>
    /// <param name="restart">Whether selection resets each motion to its first frame.</param>
    public void SetMotionAll(int motion, bool restart = true) => _fighters.SetMotionAll(motion, restart);

    /// <summary>Maps 60 Hz presentation time to the source PAL 50 Hz clock without drift.</summary>
    /// <param name="delta">Elapsed presentation time in seconds.</param>
    public void StepMotions(float delta)
    {
        var elapsed = _motionClock.Advance(delta);
        if (elapsed == 0)
        {
            return;
        }

        for (var frame = 0; frame < elapsed; frame++)
        {
            _fighters.Step(1);
            _stage?.Step(1);
            SynchronizePartnerPresentation();
        }
    }

    /// <summary>Clears only the carried fractional PAL time before an isolated production cadence trial.</summary>
    public void ResetMotionClockForProbe() => _motionClock.Reset();

    /// <summary>Each battle creates a stage task in the battle-stage presentation contract, including repeated fights on one stage.</summary>
    public void RestartEntranceFade()
    {
        _entranceFade.Restart();
        ApplyEntranceFade();
    }

    /// <summary>Probe-only comparison against the same pose with lighting and background neutralised.</summary>
    /// <param name="neutral">Whether to apply neutral lighting instead of the stage light set.</param>
    public void SetNeutralLightsForProbe(bool neutral)
    {
        if (neutral)
        {
            // Keep the stage rendered with its own LightSet. The comparison then isolates the
            // fighter silhouette instead of turning the whole background into a second variable.
            var off = new BattleModel.BattleLight(Vector3.Zero, Colors.Black);
            _fighters.SetLighting(new BattleModel.BattleLightSet([off, off, off], Colors.Black));
            return;
        }

        _entranceFade.Complete();
        ApplyEntranceFade();
    }

    /// <summary>Recreates a measured point of the source entrance fade for a visual oracle.
    /// Production always advances this value in Present.</summary>
    /// <param name="fade">Entrance-fade fraction to reproduce.</param>
    public void SetLightFadeForProbe(float fade)
    {
        _entranceFade.SetFraction(fade);
        ApplyEntranceFade();
    }

    /// <summary>Describes the loaded stage light set for diagnostics.</summary>
    /// <returns>The light directions and colours, including the ambient colour.</returns>
    public string StageLightSummary()
    {
        if (_stageLights is not { } set)
        {
            throw new InvalidOperationException("stage lights unavailable");
        }

        var rgb = set.Lights.Select(light => $"dir=({light.Direction.X:0.###},{light.Direction.Y:0.###},{light.Direction.Z:0.###}) rgb=({light.Rgb.R * 255:0},{light.Rgb.G * 255:0},{light.Rgb.B * 255:0})");
        return $"lights={set.Lights.Count} ambient=({set.Ambient.R * 255:0},{set.Ambient.G * 255:0},{set.Ambient.B * 255:0}) {string.Join(" ", rgb)}";
    }

    /// <summary>One decoded lit vertex after the same GTE-style lighting calculation used by
    /// the visible mesh. This is diagnostic evidence, never a rendering shortcut.</summary>
    /// <param name="fighterId">Identifier of the fighter that provides the diagnostic vertex.</param>
    /// <returns>A description of the selected vertex lighting result.</returns>
    public string LightingVertexSummary(int fighterId) => _fighters.Require(fighterId).LightingVertexSummary();

    /// <summary>Sets the battle camera to a decoded source view, fading from where it is.
    /// The view's GsRVIEW2 sign change and the world-coordinate conversion cancel, so
    /// decoded camPos/camRef map directly at the shared /256 scale.</summary>
    /// <param name="view">Decoded view parameters to apply.</param>
    /// <param name="frames">Number of presentation frames used for the transition.</param>
    /// <param name="snap">Whether to apply the view immediately.</param>
    public void SetSourceCamera(BattleModel.BattleCameraView view, int frames = 60, bool snap = false)
    {
        var position = BattleSceneMath.CameraPosition(view.CamPos[0], view.CamPos[1], view.CamPos[2]);
        var reference = BattleSceneMath.CameraPosition(view.CamRef[0], view.CamRef[1], view.CamRef[2]);
        _cameraFade.Begin(
            new Vector3(position.X, position.Y, position.Z),
            new Vector3(reference.X, reference.Y, reference.Z),
            view.Proj,
            frames <= 0 ? 1f : 1f / frames,
            snap);
        ApplyCamera();
        GD.Print($"BATTLE_CAMERA pos=({view.CamPos[0]},{view.CamPos[1]},{view.CamPos[2]}) " +
            $"ref=({view.CamRef[0]},{view.CamRef[1]},{view.CamRef[2]}) proj={view.Proj} " +
            $"fov={BattleSceneMath.VerticalFovDegrees(view.Proj):0.###} frames={(snap ? 0 : frames)}");
    }

    /// <summary>Applies a complete composed camera view before it is set as the reference view.
    /// This is the path used by the player command shot camera.</summary>
    /// <param name="view">Composed camera view to apply.</param>
    /// <param name="snap">Whether to apply the view immediately.</param>
    public void SetSourceCamera(SourceBattleCameraView view, bool snap = true)
    {
        var position = BattleSceneMath.ComposeCameraPosition(view, reference: false);
        var reference = BattleSceneMath.ComposeCameraPosition(view, reference: true);
        _cameraFade.Begin(
            new Vector3(position.X, position.Y, position.Z),
            new Vector3(reference.X, reference.Y, reference.Z),
            view.Projection,
            1f,
            snap);
        ApplyCamera();
    }

    /// <summary>The battle camera's interpolation: advances the fade and applies the
    /// interpolated view. Called every presented frame.</summary>
    /// <param name="delta">Elapsed presentation time in seconds.</param>
    public void StepCamera(float delta)
    {
        _cameraFade.Step(delta);
        ApplyCamera();
    }

    /// <summary>Loads the decoded battle-HUD frames (HP-bar caps, background, technique gauge,
    /// cursor) so the HUD draws the real disc sprites instead of a flat panel.</summary>
    /// <param name="hud">Extracted sprites and layout data used by the battle HUD.</param>
    public void SetHud(BattleHudData hud) => _hud.SetHud(hud);

    /// <summary>Binds the technique and item icons plus the message continuation arrow.</summary>
    /// <param name="icons">Extracted icon atlas data used by menu elements.</param>
    public void SetMenuIcons(BattleMenuIconsData icons) => _hud.SetMenuIcons(icons);

    /// <summary>Uses the decoded CMFONTTM atlas already used by DialogueRenderer. The HUD
    /// recolours the atlas pages per palette row itself, so it takes only the font data.</summary>
    /// <param name="font">Extracted font atlas used to draw HUD text.</param>
    public void SetFont(FontData font) => _hud.SetFont(font);

    /// <summary>The decoded camera views of the player's (enemy: false) or enemy's (enemy: true)
    /// model, for the source camera. Empty when that model was not built.</summary>
    /// <param name="enemy">Whether to return the opponent's cameras instead of the player's.</param>
    /// <returns>The requested fighter's decoded camera views.</returns>
    public IReadOnlyList<BattleModel.BattleCameraView> FighterCameras(bool enemy) =>
        (enemy ? _fighters.Enemy : _fighters.Player)?.Cameras ?? [];

    /// <summary>Counts polygons that retain textured primitive state.</summary>
    /// <param name="fighterId">Identifier of the fighter to inspect.</param>
    /// <returns>The number of untextured polygons that retain textured state.</returns>
    public int UntexturedPolygonCount(int fighterId) => _fighters.Find(fighterId)?.UntexturedPolygonCount ?? 0;

    /// <summary>The battle presentation starts on the enemy view: the source reads the enemy's
    /// camera count, then takes the player's camera at enemyCameraCount - 1.</summary>
    public void SetOpeningCamera()
    {
        var playerCameras = FighterCameras(enemy: false);
        var index = FighterCameras(enemy: true).Count - 1;
        if (index < 0 || index >= playerCameras.Count)
        {
            throw new InvalidOperationException("source opening camera unavailable");
        }

        SetSourceCamera(playerCameras[index], snap: true);
    }

    /// <summary>Sets the main command-menu scene camera: the shot camera that owns the full battle
    /// scene during the player's command turn (see <see cref="BattleCommandCamera"/>).</summary>
    public void SetCommandMenuCamera()
    {
        _shotClock.Reset();
        SetSourceCamera(_commandCamera.Start(FighterCameras(enemy: false), FighterCameras(enemy: true)), snap: true);
        ApplyShotVisibility();
    }

    /// <summary>Describes the active command shot camera for diagnostics.</summary>
    /// <returns>A textual summary of the shot camera state.</returns>
    public string ShotCameraSummary() => _commandCamera.Summary();

    /// <summary>Installs a deterministic command-camera random source for a probe and restores the runtime source on disposal.</summary>
    /// <param name="rng">Seeded source whose draws the caller records.</param>
    /// <returns>A scope that restores the ordinary runtime random source.</returns>
    public IDisposable OverrideShotCameraRngForProbe(IShotCameraRng rng) =>
        new ShotCameraRngScope(_commandCamera, _commandCamera.ReplaceRngForProbe(rng));

    /// <summary>Describes the active player motion and main battle camera for deterministic replay traces.</summary>
    /// <returns>A stable motion and camera projection, or inactive markers when unavailable.</returns>
    public string ReplaySummary()
    {
        var motion = _fighters.Player?.Motion;
        var motionText = motion is null ? "inactive" : $"motion={motion.Motion} frame={motion.Frame} blend={motion.Blend}";
        return $"{motionText} camera={_commandCamera.Summary()}";
    }

    /// <summary>Advances the command shot camera by elapsed source time and applies its last view.</summary>
    /// <param name="delta">Elapsed presentation time in seconds.</param>
    public void StepShotCamera(float delta)
    {
        var sourceTicks = _shotClock.Advance(delta);
        for (var tick = 0; tick < sourceTicks; tick++)
        {
            if (_commandCamera.Step() is not { } view)
            {
                return;
            }

            SetSourceCamera(view, snap: true);
            ApplyShotVisibility();
        }
    }

    /// <summary>Stops the command shot camera and restores both fighters' visibility.</summary>
    public void StopShotCamera()
    {
        _shotClock.Reset();
        if (_commandCamera.Stop() is { } overview)
        {
            SetSourceCamera(overview, snap: true);
        }

        _fighters.RestoreVisibility();
    }

    /// <summary>Offscreen acceptance oracle for a source battle view (see <see cref="BattleGeometryOracle"/>).</summary>
    /// <param name="focusedFighterId">Optional fighter expected to occupy the shot focus.</param>
    /// <returns>A description of the geometry assertion result.</returns>
    public string AssertOpeningGeometry(int? focusedFighterId = null) =>
        BattleGeometryOracle.AssertOpening(_fighters.Models, _stage, _camera, focusedFighterId);

    /// <summary>Sets the white-flash overlay level (0 = off, 1 = full white).</summary>
    /// <param name="level">Flash opacity before clamping to the valid range.</param>
    public void SetFlash(float level) => _flash.Color = new Color(1, 1, 1, Mathf.Clamp(level, 0f, 1f));

    /// <summary>Shows or hides the hit spark on the struck fighter.</summary>
    /// <param name="onEnemy">Whether the spark should target the opponent instead of the player.</param>
    /// <param name="show">Whether the spark should be visible.</param>
    public void ShowHitSpark(bool onEnemy, bool show)
    {
        if ((onEnemy ? _fighters.Enemy : _fighters.Player) is { } model)
        {
            _hitSpark.Position = BattleSceneBounds.Of(model.Root).GetCenter();
        }

        _hitSpark.Visible = show;
    }

    /// <summary>Presents one deterministic probe frame at the ordinary 60 Hz presentation interval.</summary>
    /// <param name="view">Current battle state, or <see langword="null"/> when no battle is active.</param>
    public void Present(BattleView? view) => Present(view, PresentDelta);

    /// <summary>Presents the current battle view and advances its camera, lighting, model, and HUD state.</summary>
    /// <param name="view">Current battle state, or <see langword="null"/> when no battle is active.</param>
    /// <param name="delta">Elapsed rendered-frame time.</param>
    public void Present(BattleView? view, double delta)
    {
        var elapsed = (float)delta;

        // Advance and apply the battle camera's interpolation each presented frame.
        StepCamera(elapsed);
        StepEntranceFade(elapsed);
        if (view is not { } value)
        {
            _hud.SetView(null);
            _partnerView.Visible = false;
            return;
        }

        if (!_probePoseOverride)
        {
            StepMotions(elapsed);
        }

        var commandTurn = value.Phase == BattleFlowPhase.PlayerCommand;
        if (commandTurn)
        {
            StepShotCamera(elapsed);
        }
        else
        {
            StopShotCamera();
        }

        PresentPartnerView(commandTurn);
        _modelLayer.Rebuild(_camera, _fighters.Models);
        _partnerView.Rebuild();
        LogOrdering();
        _hud.SetView(value);
    }

    /// <summary>Frees the whole battle scene.</summary>
    public void Dispose()
    {
        _partnerView.Dispose();
        _layer.QueueFree();
    }

    /// <summary>Gets a live model without changing product placement.</summary>
    /// <param name="fighter">Fighter identifier to inspect.</param>
    /// <returns>The required live model.</returns>
    internal BattleModel ModelForProbe(int fighter) => _fighters.Require(fighter);

    /// <summary>Determines whether an exception represents an expected model loading failure.</summary>
    /// <param name="error">Exception raised while loading a model document.</param>
    /// <returns><see langword="true"/> when callers may use the model fallback path.</returns>
    private static bool IsModelLoadError(Exception error) =>
        error is InvalidOperationException or FileNotFoundException or System.Text.Json.JsonException;

    /// <summary>Extracts a fighter identifier from a model document path.</summary>
    /// <param name="path">Model document path whose file name may contain an identifier.</param>
    /// <returns>The parsed identifier, or <c>-1</c> when the file name has another shape.</returns>
    private static int ParseFighterId(string path)
    {
        const string prefix = "fighter-";
        const string suffix = "-model";
        var name = Path.GetFileNameWithoutExtension(path);
        if (!name.StartsWith(prefix, StringComparison.Ordinal) || !name.EndsWith(suffix, StringComparison.Ordinal))
        {
            return -1;
        }

        return int.TryParse(name[prefix.Length..^suffix.Length], out var fighter) ? fighter : -1;
    }

    /// <summary>Validates a non-negative probe animation frame.</summary>
    /// <param name="frame">Frame number to validate.</param>
    /// <returns>The validated frame number.</returns>
    private static int ValidateProbeAnimationFrame(int frame)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(frame);
        return frame;
    }

    /// <summary>Creates the viewport camera with the default battle framing.</summary>
    /// <returns>A configured current camera for the battle viewport.</returns>
    private static Camera3D CreateCamera() => new()
    {
        Position = new Vector3(0, 6.5f, 22f),
        RotationDegrees = new Vector3(-4, 0, 0),
        Current = true,
        KeepAspect = Camera3D.KeepAspectEnum.Height,
        Fov = BattleSceneMath.VerticalFovDegrees(280),
    };

    /// <summary>No Godot lighting is attached to the battle world: model and stage meshes are
    /// unshaded and receive only the decoded stage light set.</summary>
    /// <returns>An environment with a flat background colour and no ambient light.</returns>
    private static WorldEnvironment CreateEnvironment() => new()
    {
        Environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(0.05f, 0.07f, 0.13f),
            AmbientLightSource = Godot.Environment.AmbientSource.Disabled,
        },
    };

    /// <summary>A dark floor under the models. The decoded fight stage replaces it visually, but it
    /// stays as the fallback (and to catch shadowing) when a stage is absent.</summary>
    /// <returns>A 60x40 unshaded plane 1.4 units below the arena centre.</returns>
    private static MeshInstance3D CreateFloor() => new()
    {
        Mesh = new PlaneMesh { Size = new Vector2(60, 40) },
        Position = new Vector3(0, -1.4f, -4),
        MaterialOverride = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.08f, 0.09f, 0.13f),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        },
    };

    /// <summary>The hit spark: a small billboarded glow at the struck fighter.</summary>
    /// <returns>A hidden sprite, positioned when a hit is shown.</returns>
    private static Sprite3D CreateHitSpark() => new()
    {
        Texture = BattleHitSparkTexture.Create(),
        Position = Vector3.Zero,
        Modulate = new Color(1f, 1f, 0.7f, 0.95f),
        Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
        PixelSize = 0.05f,
        Visible = false,
    };

    /// <summary>Hides UI and transient effects that do not belong in a model contact sheet.</summary>
    private void HideOverlaysForSheet()
    {
        _hud.Visible = false;
        _flash.Visible = false;
        _hitSpark.Visible = false;
    }

    /// <summary>Loads one decoded battle model at a side; returns false (and logs nothing here)
    /// when the JSON is absent or invalid, so the caller can fall back to a capsule.</summary>
    /// <param name="jsonPath">Path to the extracted model document.</param>
    /// <param name="label">Display label assigned to the model node.</param>
    /// <param name="fighterId">Identifier of the fighter represented by the model.</param>
    /// <param name="enemy">Whether the model is placed as an opponent.</param>
    /// <returns><see langword="true"/> when the model was loaded and added.</returns>
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
            _fighters.Add(model);
            ApplyStageLights();
            GD.Print($"BATTLE_GEOMETRY fighter={fighterId} height={model.Height} distance={model.Distance} " +
                $"position=({model.Root.Position.X:0.###},{model.Root.Position.Y:0.###},{model.Root.Position.Z:0.###}) " +
                $"rotation=(0,{placement.YawRadians:0.###},0)");
            return true;
        }
        catch (Exception error) when (IsModelLoadError(error))
        {
            _lastModelLoadFailure = error.Message;
            GD.PushWarning($"WARN battle-model-load-failed fighter={fighterId} enemy={enemy} reason={error.Message}");
            return false;
        }
    }

    /// <summary>Adds a visible fallback capsule when a fighter model cannot be loaded.</summary>
    /// <param name="fighterId">Identifier of the fighter represented by the fallback.</param>
    /// <param name="label">Display label assigned to the fallback node.</param>
    /// <param name="enemy">Whether the fallback is placed as an opponent.</param>
    /// <param name="color">Unshaded colour assigned to the capsule.</param>
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
        _placeholders.Add(instance);
    }

    /// <summary>Releases current fighter models and fallback capsules from the scene.</summary>
    private void ClearModels()
    {
        _modelLayer.Clear();
        _partnerView.Clear();
        _lastOrderingLog = null;
        _fighters.Clear();
        foreach (var instance in _placeholders)
        {
            instance.QueueFree();
        }

        _placeholders.Clear();
        Built = false;
    }

    /// <summary>Advances the stage entrance fade on the PAL clock, once a stage light set exists.</summary>
    /// <param name="delta">Presented time in seconds.</param>
    private void StepEntranceFade(float delta)
    {
        if (_stageLights is not null && _entranceFade.Advance(delta))
        {
            ApplyEntranceFade();
        }
    }

    /// <summary>Applies the stage light set at the current entrance-fade fraction.</summary>
    private void ApplyStageLights()
    {
        if (_stageLights is not { } source)
        {
            return;
        }

        var set = _entranceFade.Scale(source);
        _fighters.SetLighting(set);
        _partnerView.Model?.SetLighting(set);
        _stage?.SetLighting(set);
    }

    /// <summary>Applies the fade level to the lights, the stage model colour and the background.</summary>
    private void ApplyEntranceFade()
    {
        ApplyStageLights();
        _stage?.SetModelColor(_entranceFade.StageColor());
        _environment.Environment.BackgroundColor = _entranceFade.Background(_stage?.StageBgColor ?? Colors.Black);
        ApplyEntranceOverlayVisibility();
    }

    /// <summary>Prevents a retained HUD or partner silhouette from preceding a restarted stage fade.</summary>
    private void ApplyEntranceOverlayVisibility()
    {
        _hud.Visible = _hudVisibleForProbe && _entranceFade.OverlaysVisible;
        _partnerView.Visible &= _entranceFade.OverlaysVisible;
    }

    /// <summary>Applies the interpolated camera state to the battle viewport camera.</summary>
    private void ApplyCamera()
    {
        _camera.Position = _cameraFade.Position;
        _camera.LookAt(_cameraFade.Target, Vector3.Up);
        _camera.Fov = BattleSceneMath.VerticalFovDegrees(Math.Max(1, (int)MathF.Round(_cameraFade.Projection)));
    }

    /// <summary>Applies fighter visibility required by the active command shot.</summary>
    private void ApplyShotVisibility()
    {
        if (_commandCamera.Shot is { } shot)
        {
            _fighters.ApplyShotVisibility(shot.PlayerVisible, shot.EnemyVisible);
        }
    }

    /// <summary>The partner view lives for the whole command turn, submenus included, and is
    /// removed when the turn returns an action.</summary>
    /// <param name="commandTurn">True during the player's command turn.</param>
    private void PresentPartnerView(bool commandTurn)
    {
        _partnerView.Visible = _entranceFade.OverlaysVisible && commandTurn && _partnerView.Model is not null;
        if (_partnerView.Visible)
        {
            _partnerView.AimCamera(_fighters.Player?.Motion?.IdleMotion ?? 0);
        }
    }

    /// <summary>Keeps the independent partner target on the active fighter's committed pose and face frame.</summary>
    private void SynchronizePartnerPresentation()
    {
        if (_partnerView.Model is { } partner && _fighters.Player is { } player)
        {
            partner.SynchronizePresentationFrom(player);
        }
    }

    /// <summary>Logs ordering statistics only when their values changed.</summary>
    private void LogOrdering()
    {
        (int Polygons, int Culled, int UnsupportedAbr) ordering = (_modelLayer.PolygonCount, _modelLayer.CulledCount, _modelLayer.UnsupportedAbrCount);
        if (_lastOrderingLog != ordering)
        {
            _lastOrderingLog = ordering;
            GD.Print($"battle-ot polygons={ordering.Polygons} culled={ordering.Culled} unsupported-abr={ordering.UnsupportedAbr}");
        }
    }
}
