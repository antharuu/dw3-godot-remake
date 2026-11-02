using System.Diagnostics;
using Godot;
using Dw3.Data;
using Microsoft.Data.Sqlite;
using Dw3Production.Conversion;
using Dw3Production.Diagnostics;
using Dw3Production.Diagnostics.Battle;
using Dw3Production.Diagnostics.Field;
using Dw3Production.Input;
using Dw3Production.Logging;
using Dw3Production.Presentation;
using Dw3Production.Scene;
using Dw3Production.Simulation;
using Dw3Production.Startup;
using NumericsVector2 = System.Numerics.Vector2;

namespace Dw3Production;

/// <summary>
/// Godot composition root for one playable session. It owns node lifecycle and composes
/// data-pack readers, simulation controllers, presenters, input, and optional diagnostic probes.
/// It is not the authority for movement, warp, dialogue, battle, or rendering rules: those stay
/// in the corresponding Simulation and Presentation collaborators.
/// </summary>
/// <remarks>
/// Startup validates the local data pack, loads runtime assets, creates presenters, applies the
/// initial field scene, then optionally dispatches one diagnostic probe. Interactive frames accept
/// Godot input, run fixed simulation steps, and present the resulting field or battle view.
///
/// Responsibilities are grouped below: composition state; startup and lifecycle; field and battle
/// coordination; field interaction and dialogue; and scene/cache transitions. Diagnostic selection
/// lives behind <see cref="IDiagnosticProbeHost"/> in the Diagnostics folder, and the large probe
/// implementations are split into diagnostic partial files so the normal runtime loop stays legible.
/// </remarks>
public partial class GameHost : Node2D, IDiagnosticProbeHost
{

    #region Composition state

    private readonly GameState _state = new();
    private readonly SimClock _clock = new();

    private readonly ClimbController _climb = new();
    private readonly DropController _drop = new();
    private readonly InputAdapter _input = new();
    private readonly QuietLog _log = new();
    private readonly HostStartupOptions _options = HostStartupOptions.FromEnvironment();
    private readonly WarpController _warp = new();
    private readonly PlayerIconController _icon = new();
    private BattleDataPackLoader? _battleLoader;
    private string _dataRoot = string.Empty;
    private FieldDataPackLoader? _fieldLoader;
    private FieldAnimationDataPackLoader? _animationLoader;
    private FieldZoneCache? _zoneCache;
    private FieldZone? _currentZone;
    private FloorMap? _floor;
    private IReadOnlyList<Occupant> _occupants = [];
    private PlayerData? _playerData;
    private ClimbData? _actionData;
    private WaterData? _water;
    private NpcData? _npc;
    private PlayerIconData? _iconData;
    private FieldRenderer? _renderer;
    private DialogueData? _dialogueData;
    private readonly DialogueController _dialogue = new();
    private DialogueRenderer? _dialogueRenderer;
    private DialogueFrameData? _dialogueFrames;
    private Dictionary<int, Texture2D>? _frameTextures;
    private Texture2D? _panelTexture;
    private int _dialogueBoxWidth;
    // Explicit test-only driver. Interactive sessions never set this flag and never receive
    // injected input, repositioning, or an automatic route.
    private bool _testDriverActive;

    // M6 combat runtime: the engine-free encounter controller owns the step countdown and
    // the area/event draws; the loaded encounter/fighter/tech manifests fill BattleSetup.
    private readonly EncounterController _encounter = new(new SystemRandomEncounterRng());
    private EncounterData? _encounterData;
    private FighterData? _fighterData;
    private TechniqueData? _techData;
    private DigivolutionData? _digivolutionData;
    private BattleItemsData? _battleItemsData;
    private BattleTextData? _battleTextData;
    private AreaMap? _areas;
    private BattleSetup? _activeBattle;
    private BattleFlowController? _battleFlow;
    private readonly BattleMenuController _battleMenu = new();
    private readonly IBattleRng _battleRng = new SystemRandomBattleRng();
    private BattleRenderer? _battleRenderer;
    private FightReportRenderer? _fightReportRenderer;
    private FightReportController? _fightReportController;
    private FightReportInput? _fightReportInput;
    private int _battleReportFadeTicks;
    private int _battleStepCounter;
    private int _lastBattleEncounter = -1;
    private int[] _lastBattleEnemyIds = [];
    private IReadOnlyDictionary<int, int> _battleNameIds = new Dictionary<int, int>();
    // The battle menu's press-edge state, so the cursor advances once per key press, not
    // once per frame while a key is held. Reset on battle start.
    private bool _battlePrevUp;
    private bool _battlePrevDown;
    private int _battleCatalogForId = -1;
    private int _battleRenderedPlayerId = -1;
    private int _battleRenderedEnemyId = -1;
    private bool _battlePlayerCameraActive;
    private readonly List<int> _battleSubmenuArgs = [];
    // switch_in_the battle presentation flow keeps the selected party member while its second page chooses the
    // incoming form and then Changement/Technique de paire.
    private int _battleSwitchPartner = -1;
    private int _battlePendingChangeMotion;
    private bool _battleSwitchForced;
    // The diagnostic on-screen notice for a failed battle load, plus its last text (the
    // probe asserts both the ERROR log and the notice).
    private Label? _battleNoticeLabel;
    private string? _battleNoticeText;

    // M6-C6: the battle rewards + growth. The rewards manifest and growth tables load at
    // startup; the persistent test-team growth state tracks each partner's exp/level across
    // the session's battles (there is no save system). The resolved report is carried into the
    // battle view while the terminal report holds, then discarded on return.
    private RewardsData? _rewardsData;
    private GrowthTables? _growthTables;
    private IReadOnlyList<int>? _partyDigimonIds;
    private IReadOnlyList<PartnerGrowthState>? _partyGrowth;
    private BattleReportView? _battleReport;
    private bool _battleResolved;
    // The title screen shown on defeat (MODE_TITLE): a terminal modal state, distinct from the
    // field, that the probe asserts.
    private CanvasLayer? _titleScreen;
    private bool _inTitle;

    // These accessors mark lifecycle boundaries explicitly. A caller that needs an
    // initialized runtime collaborator receives a useful failure instead of masking
    // a violated startup invariant with the null-forgiving operator.
    private FieldZone CurrentZone => _currentZone ?? throw new InvalidOperationException("field scene has not been applied");

    private FieldRenderer Renderer => _renderer ?? throw new InvalidOperationException("field renderer has not been initialized");

    private BattleFlowController BattleFlow => _battleFlow ?? throw new InvalidOperationException("battle flow has not been initialized");

    private BattleRenderer BattleRenderer => _battleRenderer ?? throw new InvalidOperationException("battle renderer has not been initialized");

    private BattleDataPackLoader BattleLoader => _battleLoader ?? throw new InvalidOperationException("battle data has not been initialized");

    private FighterData FighterData => _fighterData ?? throw new InvalidOperationException("fighter data has not been initialized");

    private TechniqueData TechniqueData => _techData ?? throw new InvalidOperationException("technique data has not been initialized");

    private DigivolutionData DigivolutionData => _digivolutionData ?? throw new InvalidOperationException("digivolution data has not been initialized");

    private BattleItemsData BattleItemsData => _battleItemsData ?? throw new InvalidOperationException("battle item data has not been initialized");

    private FloorMap Floor => _floor ?? throw new InvalidOperationException("field floor has not been initialized");

    private DialogueRenderer DialogueRenderer => _dialogueRenderer ?? throw new InvalidOperationException("dialogue renderer has not been initialized");

    private FieldDataPackLoader FieldLoader => _fieldLoader ?? throw new InvalidOperationException("field data has not been initialized");

    private ClimbData ActionData => _actionData ?? throw new InvalidOperationException("action data has not been initialized");

    private FieldZoneCache ZoneCache => _zoneCache ?? throw new InvalidOperationException("zone loader or npc data unavailable before host ready");

    private Texture2D PanelTexture => _panelTexture ?? throw new InvalidOperationException("dialogue panel texture has not been initialized");

    #endregion

    #region Startup and Godot lifecycle

    /// <summary>
    /// Validates the user data pack and starts a playable session, or displays the first-launch
    /// extraction screen when the pack is unavailable or invalid. Successful startup constructs
    /// runtime collaborators, applies an initial scene, and may dispatch one opt-in diagnostic mode.
    /// </summary>
    public override void _Ready()
    {
        string dataRoot = UserPaths.GetDataDirectory();

        // If the pack is missing, has an incompatible schema, or is corrupt, show the
        // first-launch extraction screen instead of crashing. Once it emits PackReady,
        // StartupWithDataPack continues normal initialization.
        if (!DataPackValidator.IsUsable(dataRoot))
        {
            ShowFirstLaunchScreen(dataRoot);
            return;
        }

        StartupWithDataPack(dataRoot);
    }

    private void ShowFirstLaunchScreen(string dataRoot)
    {
        // Resolve the extractor binary: DW3_EXTRACTOR env var (development) or the debug
        // build output next to the extractor project (after dotnet build Dw3.sln).
        string repoRoot = Path.GetFullPath(
            Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", ".."));
        string extractorPath = SystemExtractorLauncher.ResolveExtractorPath(repoRoot);

        var screen = new FirstLaunchScreen(dataRoot, extractorPath);
        screen.PackReady += () =>
            Callable.From(() => OnFirstLaunchComplete(dataRoot)).CallDeferred();
        AddChild(screen);
    }

    private async void OnFirstLaunchComplete(string dataRoot)
    {
        // Remove the first-launch screen, then initialize the game normally.
        foreach (Node child in GetChildren())
        {
            if (child is FirstLaunchScreen)
            {
                child.QueueFree();
                break;
            }
        }
        StartupWithDataPack(dataRoot);

        // Probe: after the first-launch extraction flow, capture S200 to prove the game
        // reached the field.  Guarded by DW3_FIRST_LAUNCH_PROBE; no effect on nominal play.
        if (_options.FirstLaunchProbe)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            var captureDir = ProbeOutputRoot(dataRoot);
            string capturePath = Path.Combine(captureDir, "first-launch-s200.png");
            var image = GetViewport().GetTexture().GetImage();
            Error saveErr = image.SavePng(capturePath);
            GD.Print(saveErr == Error.Ok
                ? $"FIRST_LAUNCH_PROBE capture OK: {capturePath}"
                : $"FIRST_LAUNCH_PROBE capture FAIL ({saveErr}): {capturePath}");
            GD.Print("FIRST_LAUNCH_PROBE PASS");
            GetTree().Quit(0);
        }
    }

    private void StartupWithDataPack(string dataRoot)
    {
        try
        {
            var startupWatch = Stopwatch.StartNew();
            _dataRoot = dataRoot;
            var assets = RuntimeAssetsLoader.Load(dataRoot);
            AdoptAssets(assets);
            StoryFixture.Apply(_state);
            _state.IsRunning = true;
            _encounter.RandomBattles = true;
            _encounter.RollBattleSteps();
            BuildBattleNotice();
            BuildTitleScreen();
            _renderer = new FieldRenderer(this, assets.ExitCue, assets.Water, assets.Icon);
            BuildDialoguePresentation(assets.Dialogue, assets.DialogueFrames);
            EnterStartZone(startupWatch);
            if (_options.StartF3)
            {
                ToggleF3();
            }

            if (!DispatchDiagnosticProbe())
            {
                CallDeferred(MethodName.CaptureSpawn);
            }
        }
        catch (CacheResourceException error)
        {
            GD.PushError(error.Message);
            GetTree().Quit(2);
        }
        catch (Exception error)
        {
            GD.PushError($"CACHE_INVALID: host initialization failed: {error.Message}");
            GetTree().Quit(2);
        }
    }

    /// <summary>
    /// Keeps the startup data for the session: the three open pack readers (disposed in
    /// <see cref="_ExitTree"/>), the tables the field and battle rules read, and the zone cache
    /// that builds zones lazily from the field reader.
    /// </summary>
    /// <param name="assets">Startup data read from the pack.</param>
    private void AdoptAssets(RuntimeAssets assets)
    {
        _fieldLoader = assets.FieldLoader;
        _animationLoader = assets.AnimationLoader;
        _battleLoader = assets.BattleLoader;
        _playerData = assets.Player;
        _actionData = assets.Actions;
        _climb.SetData(assets.Climb);
        _drop.SetData(assets.Drop);
        _water = assets.Water;
        _npc = assets.Npc;
        _iconData = assets.Icon;
        _icon.SetAnimation(assets.Icon.Animations[0]);
        _encounterData = assets.Encounters;
        _fighterData = assets.Fighters;
        _techData = assets.Techniques;
        _digivolutionData = assets.Digivolutions;
        _battleItemsData = assets.BattleItems;
        _battleTextData = assets.BattleText;
        _rewardsData = assets.Rewards;
        _growthTables = assets.GrowthTables;
        _partyDigimonIds = assets.PartyDigimonIds;
        _partyGrowth = assets.PartyGrowth;
        _zoneCache = new FieldZoneCache(assets.FieldLoader, assets.Npc.Occupants);
    }

    /// <summary>Creates the dialogue presenter and uploads the font pages and talk-box sprites as textures.</summary>
    /// <param name="dialogue">Conversations and dialogue font.</param>
    /// <param name="frames">Talk-box frame sprites and panel.</param>
    private void BuildDialoguePresentation(DialogueData dialogue, DialogueFrameData frames)
    {
        _dialogueData = dialogue;
        _dialogueRenderer = new DialogueRenderer(this);
        DialogueRenderer.SetFont(dialogue.Font, PackTextures.Load(dialogue.Font.Page0Path), PackTextures.Load(dialogue.Font.Page1Path));
        var frameTextures = new Dictionary<int, Texture2D>();
        foreach (var (frameId, frame) in frames.Frames)
        {
            frameTextures[frameId] = PackTextures.Load(frame.Path);
        }

        _panelTexture = PackTextures.Load(frames.Panel.Path);
        _dialogueFrames = frames;
        _frameTextures = frameTextures;
        DialogueRenderer.SetFrames(frames, frameTextures, _panelTexture);
    }

    /// <summary>
    /// Places the player at the session start and applies its zone. Only that zone is built at
    /// startup; the others are built on demand when a warp first reaches them.
    /// </summary>
    /// <param name="startupWatch">Clock started with the session, logged as the startup time.</param>
    /// <exception cref="CacheResourceException">The selected start zone, position or facing is refused.</exception>
    private void EnterStartZone(Stopwatch startupWatch)
    {
        var start = new SessionStartResolver(_options, FieldLoader.ZoneIds).Resolve(GetOrLoadZone);
        _state.ZoneId = start.ZoneId;
        _state.Position = start.Position;
        _state.SetFacing(start.Facing);
        _state.AnimationSet = 1;
        _state.AnimationDirection = _state.Facing;
        if (start.IsDiagnostic)
        {
            Renderer.DiagnosticBadge = $"DIAGNOSTIC zone={start.ZoneId}";
        }

        ApplyScene(GetOrLoadZone(start.ZoneId));
        var label = start.IsDiagnostic ? "diagnostic" : "host ready";
        QuietLog.Info($"{label} zone={_state.ZoneId} position=({_state.Position.X:0},{_state.Position.Y:0}) facing={_state.Facing} progress=0x{_state.Progress:X2} zones={ZoneCache.Count} decoded={ZoneCache.DecodeCount} npcs={_npc?.Occupants.Count} startup_ms={startupWatch.ElapsedMilliseconds}");
    }

    /// <summary>Starts the diagnostic probe selected by the environment, in the established family order.</summary>
    /// <returns><see langword="true"/> when a probe was started and owns the rest of the session.</returns>
    private bool DispatchDiagnosticProbe() =>
        new FieldDiagnosticProbeRunner(this).TryDispatch(_options)
        || new DialogueDiagnosticProbeRunner(this).TryDispatch(_options)
        || new BattleDiagnosticProbeRunner(this).TryDispatch(_options)
        || new StorageDiagnosticProbeRunner(this).TryDispatch(_options);

    void IDiagnosticProbeHost.RunInputProbeAndQuit()
    {
        InputMappingProbe.Run();
        GetTree().Quit(0);
    }

    void IDiagnosticProbeHost.ScheduleProbe(string methodName, bool ownsInput)
    {
        _testDriverActive = ownsInput;
        if (FieldProbeCatalog.Create(methodName, this) is { } fieldProbe)
        {
            Callable.From(() => _ = fieldProbe.RunAsync()).CallDeferred();
            return;
        }

        if (BattleProbeCatalog.Create(methodName, new BattleProbeContext(this)) is { } battleProbe)
        {
            Callable.From(() => _ = battleProbe.RunAsync()).CallDeferred();
            return;
        }

        CallDeferred(methodName);
    }

    /// <summary>
    /// Forwards interactive Godot input to the input adapter. Scripted probes own their injected
    /// input and suppress engine-delivered events to keep their deterministic route intact.
    /// </summary>
    public override void _Input(InputEvent inputEvent)
    {
        // The scripted test driver injects input exclusively through _input.Handle.
        // While it runs, ignore engine-delivered events: under Xvfb the X11 backend
        // can synthesize stray key events during an awaited FramePostDraw, which
        // otherwise corrupt the driver's held-direction/action state and make the
        // shared warp helpers flaky. Interactive mode (_testDriverActive=false)
        // keeps normal engine input handling.
        if (_testDriverActive) return;
        _input.Handle(inputEvent);
    }

    /// <summary>
    /// Synchronizes the input adapter with application focus so no held action survives a focus
    /// loss and becomes an unintended action when the window regains focus.
    /// </summary>
    public override void _Notification(int what)
    {
        if (what == NotificationWMWindowFocusOut)
        {
            _input.SetFocused(false);
            QuietLog.Info("input focus=out cleared=true");
        }
        else if (what == NotificationWMWindowFocusIn)
        {
            _input.SetFocused(true);
            QuietLog.Info("input focus=in awaiting-new-events=true");
        }
    }

    /// <summary>
    /// Disposes data-pack readers opened for this session before Godot removes the host node.
    /// </summary>
    public override void _ExitTree()
    {
        _battleLoader?.Dispose();
        _battleLoader = null;
        _fieldLoader?.Dispose();
        _fieldLoader = null;
        _animationLoader?.Dispose();
        _animationLoader = null;
    }

    /// <summary>
    /// Advances an interactive session. It consumes fixed simulation ticks first, then presents one
    /// final field or battle state per rendered frame so rendering does not multiply after a stall.
    /// </summary>
    public override void _Process(double delta)
    {
        if (_testDriverActive || !_input.HasFocus) return;

        // On defeat the game is on the title screen (MODE_TITLE), not the field: keep the
        // overlay visible and skip the field frame.
        if (_inTitle)
        {
            _titleScreen?.Visible = true;
            return;
        }

        // F8 (debug battle test) works from any map, even before the first SimulateStep.
        if (_input.State.ConsumeF8Pressed()) StartDebugBattle();

        // A failed early init (e.g. a refused diagnostic zone) leaves no active field;
        // skip the frame instead of driving SimulateStep against a null scene.
        if (_currentZone is null) return;

        var steps = _clock.Tick(delta);
        if (_clock.LastDroppedTicks > 0) _log.Warn("sim-stall", $"simulation stall dropped_ticks={_clock.LastDroppedTicks} cap={SimClock.MaxTicksPerFrame}");
        for (var step = 0; step < steps; step++)
            SimulateStep(movement => _log.Warn($"move:{movement.Rejection}", $"move reject={movement.Rejection} zone={_state.ZoneId} position=({_state.Position.X:0.##},{_state.Position.Y:0.##})"));
        // Rendering is cadence-driven, not catch-up-tick-driven. A stall may simulate up to
        // four fixed steps but presents one final state, so F3 cannot multiply render work.
        if (_activeBattle is not null)
            _battleRenderer?.Present(BuildBattleView());
        else
        {
            _renderer?.Present(_state, CurrentZone, _input.F3On, delta, _icon.View);
            _dialogueRenderer?.Present(_dialogue.IsActive ? BuildDialogueView() : (DialogueView?)null);
        }
    }

    #endregion

    #region Field simulation and battle coordination

    /// <summary>
    /// Executes one fixed field tick in its required order: modal dialogue gate, traversal or
    /// movement, warp and action processing, diagnostic edges, animation presentation timing, then
    /// encounter selection. Presentation receives the completed state only after this method returns.
    /// </summary>
    private void SimulateStep(Action<MovementResult> onMovementRejection)
    {
        if (_inTitle) return;  // the title screen is a terminal modal state: no field simulation
        if (_floor is null || _currentZone is null) throw new InvalidOperationException("simulation field unavailable");
        if (_activeBattle is not null)
        {
            // The real battle loop: the menu consumes Up/Down/Cross/Circle, each command
            // resolves a turn, and a terminal outcome ends the battle and returns to the
            // field. Locomotion, warps, dialogue and climb/drop are all suspended.
            TickBattle();
            _ = _input.State.ConsumeF3Toggled();
            return;
        }
        if (_dialogue.IsActive)
        {
            // A conversation is a modal simulation state: locomotion, warp offers and
            // climb/drop are all suspended while it runs (the source's acting flag). The
            // dialogue machine consumes its own Cross edges and rearms on release, so the
            // same press that closes it can never immediately reopen it.
            _dialogue.Tick(_input.State, QuietLog.Info, _log.Warn);
        }
        else if (_climb.IsActive || _drop.IsActive)
        {
            // Climb/drop actions are distinct simulation states: locomotion and trigger offers
            // are suspended until the action restores control.
            if (_climb.IsActive) _climb.Tick(_state, _input.State);
            else _drop.Tick(_state, _input.State);
            // When a reversal lands back on the slot's own foot, the actor can finish
            // inside the wall's collision circle (the foot cell itself is open, but the
            // circle overlaps the wall one row above). Nudge it back to the nearest free
            // cell so the player is never stuck at the climb exit.
            if (!_climb.IsActive && !_drop.IsActive) DepenetrateFromWall();
        }
        else
        {
            if (!_warp.InputLocked)
            {
                // Dialogue starts from a fresh Cross edge while the warp is Idle (source:
                // Cross while walking -> talkToActorAt; the warp's commit is only reached from
                // Offer). When a talk starts, locomotion is skipped this tick so the same Cross
                // never both talks and moves the player.
                if (_warp.State == WarpState.Idle && _input.State.ConsumeActionPressed() && TryStartDialogue())
                {
                    // talk started; no locomotion this tick
                }
                else
                {
                    // Original pad movement defaults to ACTOR_RUN. Shift is only the explicit
                    // local walk modifier and focus loss clears it through InputAdapter.
                    _state.IsRunning = !_input.State.WalkHeld;
                    var movement = Locomotion.Tick(_state, _input.State.Direction, _floor, _occupants);
                    if (movement.Rejection is not null) onMovementRejection(movement);
                    else if (movement.Moved) CountEncounterStep();
                }
            }
            // The warp state machine runs every step while not climbing, including its
            // input-locked LockInput→Commit→Rearm transitions (only locomotion is gated).
            _warp.Tick(_state, _input.State, _currentZone, new WarpCallbacks(ResolveTarget, ApplyScene, QuietLog.Info, _log.Warn, StartClimb, StartDrop));
        }
        _ = _input.State.ConsumeF3Toggled();
        _state.AnimationTicks++;
        _icon.Advance(_state.Position);
        _renderer?.AdvanceSimulation(_state, _currentZone);
    }

    // The debug F8 battle mirrors the original battle test (debug battle setup
    // WFIGHTTS): a real manifest encounter, only the zone restrictions are ignored.
    private const int DebugBattleEncounter = 200;
    // the battle presentation flow:527-529 fades the terminal battle to black over ten PAL frames before STFGTREP.
    private const int BattleReportFadeFrames = 10;

    /// <summary>FIELDSTG_countBattleSteps: one battle step every 8 movement frames while the
    /// player walks inside a battle area. The controller owns the countdown and the draws.</summary>
    private void CountEncounterStep()
    {
        if (_areas is null) return;  // safe zone: no FIELD_MAP_AREAS plane
        _battleStepCounter++;
        if ((_battleStepCounter & 7) != 0) return;  // source: actor->counter & 7 == 0
        var areaCell = _areas.CellAt(_state.Position);
        var record = _encounter.CountBattleSteps(areaCell);
        if (record is not null) StartBattle(record);
    }

    private void StartBattle(EncounterRecord record)
    {
        try
        {
            EnterBattle(BuildEncounterBattle(record.Battle.Encounter), record.Battle.Encounter,
                $"walk encounter={record.Battle.Encounter} kind={record.Kind}");
        }
        catch (Exception error)
        {
            BattleLoadFailed(error);
        }
    }

    private void StartDebugBattle()
    {
        try
        {
            EnterBattle(BuildEncounterBattle(DebugBattleEncounter), DebugBattleEncounter, "F8 battle test");
        }
        catch (Exception error)
        {
            BattleLoadFailed(error);
        }
    }

    /// <summary>Builds the real 1v1 battle for a manifest encounter: the debug-only test team
    /// (balanced starter pack at level 25) against the encounter's enemies, then starts the flow.</summary>
    private BattleFlowController BuildEncounterBattle(int encounterIndex)
    {
        if (_fighterData is null) throw new CacheResourceException("CACHE_RESOURCE_UNAVAILABLE", "fighter manifest unavailable");
        if (_techData is null) throw new CacheResourceException("CACHE_RESOURCE_UNAVAILABLE", "tech manifest unavailable");
        if (_encounterData is null) throw new CacheResourceException("CACHE_RESOURCE_UNAVAILABLE", "encounter manifest unavailable");
        if (_digivolutionData is null) throw new CacheResourceException("CACHE_RESOURCE_UNAVAILABLE", "digivolution manifest unavailable");
        if (_battleItemsData is null) throw new CacheResourceException("CACHE_RESOURCE_UNAVAILABLE", "battle items manifest unavailable");
        var party = BattleSetupFactory.StarterPartnerDigimonIds(_fighterData);
        if (party.Count != 3) throw new CacheResourceException("CACHE_INVALID", "the balanced starter party could not be resolved");

        var fighters = _fighterData;
        var techs = _techData;
        var encounters = _encounterData;
        _battleNameIds = BuildBattleNameIds(party, fighters, encounters, encounterIndex);

        var entry = encounters.Encounters.FirstOrDefault(candidate => candidate.Index == encounterIndex)
            ?? throw new InvalidOperationException($"encounter {encounterIndex} not in the manifest");
        var setup = new BattleSetup
        {
            Battle = encounterIndex,
            Stage = ResolveBattleStage(encounterIndex),
            EncounterKind = entry.Kind,
            Partners = BattleSetupFactory.BuildParty(fighters, TestTeamGrowth.Level, party),
            Enemies = BattleSetupFactory.BuildEnemies(encounters, encounterIndex, fighters),
            Blocks = new int[12],
        };
        for (var i = 0; i < Math.Min(entry.Blocks.Count, setup.Blocks.Length); i++)
            setup.Blocks[i] = entry.Blocks[i];

        var state = BattleState.InitFighters(setup, digimon: party[0]);
        var flow = BattleSetupFactory.BuildFlow(fighters, techs, encounters, encounterIndex, party, _battleNameIds,
            new BattleTextCatalog(_battleTextData ?? throw new CacheResourceException("CACHE_RESOURCE_UNAVAILABLE", "battle text manifest unavailable")));
        WireBattleCatalogs(flow);
        flow.Start(state);
        return flow;
    }

    private void EnterBattle(BattleFlowController flow, int encounter, string why)
    {
        _activeBattle = new BattleSetup { Battle = encounter };
        _battleFlow = flow;
        _lastBattleEncounter = encounter;
        _lastBattleEnemyIds = [.. flow.State.Enemies.Select(enemy => enemy?.Id ?? 0)];
        _battleCatalogForId = -1;
        _battlePrevUp = false;
        _battlePrevDown = false;
        _battleReport = null;
        _fightReportController = null;
        _fightReportInput = null;
        _battleReportFadeTicks = 0;
        _battleResolved = false;
        _battlePlayerCameraActive = false;
        _state.BattleActive = true;

        // Build the presentation from the source fighter ids of the active partner and enemy.
        if (_battleRenderer is null)
        {
            _battleRenderer = new BattleRenderer(this);
            var playerId = flow.State.Players[0]?.Id ?? 0;
            var enemyId = flow.State.Enemies.FirstOrDefault(enemy => enemy is not null)?.Id ?? 0;
            _battleRenderer.BuildModels(ResolveBattleModelPath(playerId), ResolveBattleModelPath(enemyId),
                NameOf(playerId), NameOf(enemyId), enemyId);
            _battleRenderedPlayerId = playerId;
            _battleRenderedEnemyId = enemyId;
            // The decoded battle-HUD sprites (HP-bar window frames, technique gauge,
            // cursor) from the disc, drawn instead of the flat procedural panel.
            _battleRenderer.SetHud(BattleLoader.LoadBattleHud());
            _battleRenderer.SetMenuIcons(BattleLoader.LoadBattleMenuIcons());
            var font = _dialogueData?.Font ?? throw new InvalidOperationException("battle font unavailable");
            _battleRenderer.SetFont(font);
            _fightReportRenderer = new FightReportRenderer(this);
            _fightReportRenderer.Configure(BattleLoader.LoadBattleReportSprites(), font);
        }
        // The fight stage background resolved from the validated data pack.
        // Built once per renderer; a later battle with a different stage is a gap for the
        // F8 single-encounter test path.
        if (!_battleRenderer.StageBuilt)
            _battleRenderer.BuildStage(ResolveBattleStagePath(ResolveBattleStage(encounter)), ResolveBattleStage(encounter));
        RefreshBattlePresentationModels();
        _battleRenderer.RestartEntranceFade();
        _battleRenderer.SetOpeningCamera();
        _battleRenderer.Show();
        RefreshActivePartnerCatalogs();
        _battleMenu.OpenRoot(flow.RootMenuLines());
        QuietLog.Info($"battle-start {why} encounter={encounter} enemies=[{string.Join(",", _lastBattleEnemyIds)}] party=[{string.Join(",", flow.State.Players.Where(p => p is not null).Select(p => p!.Id))}] level={TestTeamGrowth.Level}");
    }

    private void BattleLoadFailed(Exception error)
    {
        _activeBattle = null;
        _battleFlow = null;
        _state.BattleActive = false;
        _battleRenderer?.Hide();
        QuietLog.Error($"battle-load-failed reason={error.Message}");
        _battleNoticeText = $"Combat indisponible : {error.Message}";
        if (_battleNoticeLabel is not null)
        {
            _battleNoticeLabel.Text = _battleNoticeText;
            _battleNoticeLabel.Visible = true;
        }
        QuietLog.Info("battle-load-failed return-to-field");
    }

    private void EndBattle()
    {
        var outcome = _battleFlow?.Outcome ?? BattleOutcome.None;
        _battleRenderer?.Hide();
        _fightReportRenderer?.Hide();
        _activeBattle = null;
        _battleFlow = null;
        _battleReport = null;
        _fightReportController = null;
        _fightReportInput = null;
        _battleResolved = false;
        _state.BattleActive = false;
        // The battle never moves the player, so a win or a flight returns to the field at the
        // previous position. A defeat reaches the title screen (MODE_TITLE), not the field, as
        // the battle report layout contract/the battle presentation flow request MODE_TITLE on BATTLE_LOST.
        if (outcome == BattleOutcome.Lost)
        {
            _inTitle = true;
            _titleScreen?.Visible = true;
            QuietLog.Info($"battle-end outcome={outcome} return-to-title zone={_state.ZoneId}");
        }
        else
        {
            QuietLog.Info($"battle-end outcome={outcome} return-to-field zone={_state.ZoneId} position=({_state.Position.X:0},{_state.Position.Y:0})");
        }
    }

    /// <summary>The on-screen diagnostic notice shown when a battle fails to load. It is a
    /// transient full-width label over the still-visible field, never a black screen.</summary>
    private void BuildBattleNotice()
    {
        var layer = new CanvasLayer { Layer = 200 };
        AddChild(layer);
        _battleNoticeLabel = new Label
        {
            Visible = false,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _battleNoticeLabel.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        _battleNoticeLabel.OffsetTop = 8;
        _battleNoticeLabel.OffsetBottom = 48;
        _battleNoticeLabel.AddThemeFontSizeOverride("font_size", 20);
        _battleNoticeLabel.AddThemeColorOverride("font_color", new Color(1f, 0.4f, 0.35f));
        _battleNoticeLabel.AddThemeStyleboxOverride("normal", new StyleBoxFlat
        {
            BgColor = new Color(0.06f, 0.03f, 0.03f, 0.88f),
        });
        layer.AddChild(_battleNoticeLabel);
    }

    /// <summary>The title screen shown on defeat (MODE_TITLE). A terminal modal overlay, distinct
    /// from the field: it is visible only when <see cref="_inTitle"/> is set by EndBattle.</summary>
    private void BuildTitleScreen()
    {
        _titleScreen = new CanvasLayer { Layer = 400, Visible = false };
        AddChild(_titleScreen);
        var backdrop = new ColorRect { Color = new Color(0.02f, 0.02f, 0.05f, 1f) };
        backdrop.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _titleScreen.AddChild(backdrop);
        var title = new Label
        {
            Text = "DIGIMON WORLD 2003",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        title.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        title.AddThemeFontSizeOverride("font_size", 56);
        title.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.3f));
        _titleScreen.AddChild(title);
        var subtitle = new Label
        {
            Text = "Défaite — Retour au titre",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        subtitle.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        subtitle.OffsetTop = 220;
        subtitle.AddThemeFontSizeOverride("font_size", 24);
        subtitle.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.95f));
        _titleScreen.AddChild(subtitle);
    }

    /// <summary>One fixed tick of the battle: the menu consumes semantic input and resolves a
    /// command on confirm; the enemy turn resolves one basic attack; a terminal outcome ends
    /// the battle and returns to the field.</summary>
    private void TickBattle()
    {
        var flow = _battleFlow;
        if (flow is null) { EndBattle(); return; }
        if (_fightReportController is not null)
        {
            _fightReportController.Step(_input.State.ConsumeActionPressed());
            _fightReportRenderer?.Present(_fightReportController.View, BattleText(), BuildReportNames());
            if (_fightReportController.View.Done) EndBattle();
            return;
        }
        if (flow.Phase == BattleFlowPhase.PlayerCommand && !_battlePlayerCameraActive)
        {
            _battleRenderer?.SetCommandMenuCamera();
            _battlePlayerCameraActive = true;
        }
        if (flow.Phase == BattleFlowPhase.PlayerCommand) TickBattleMenu();
        else if (flow.Phase == BattleFlowPhase.EnemyTurn)
        {
            _battlePlayerCameraActive = false;
            flow.EnemyAct(_battleRng);
        }
        RefreshBattlePresentationModels();
        if (flow.Phase == BattleFlowPhase.Ended)
        {
            if (!_battleResolved)
            {
                ResolveBattleOutcome(flow);
                _battleResolved = true;
            }
            if (flow.Outcome == BattleOutcome.Won)
            {
                if (++_battleReportFadeTicks < BattleReportFadeFrames) return;
                _battleRenderer?.Hide();
                _fightReportRenderer?.Show();
                _fightReportController = BuildFightReportController(flow);
                foreach (var log in _fightReportController.Logs) _log.Warn(log, log);
            }
            else EndBattle();
        }
    }

    /// <summary>Replaces the visible models only when combat changes an active fighter.
    /// fighter metadata resolves the requested BattleFighter.Id directly via FighterEntry.id and
    /// fighter replacement flow swaps the new model into slot 0. ClearModels queues all old roots, so
    /// changing presentation cannot leak old player or enemy nodes.</summary>
    private void RefreshBattlePresentationModels()
    {
        var flow = _battleFlow;
        if (flow is null || _battleRenderer is null) return;
        var playerId = flow.State.ActivePlayerFighter?.Id ?? 0;
        var enemyId = flow.State.ActiveEnemyFighter?.Id ?? 0;
        if (playerId == 0 || enemyId == 0 || (playerId == _battleRenderedPlayerId && enemyId == _battleRenderedEnemyId)) return;
        _battleRenderer.BuildModels(ResolveBattleModelPath(playerId), ResolveBattleModelPath(enemyId),
            NameOf(playerId), NameOf(enemyId), enemyId);
        if (_battlePendingChangeMotion != 0)
        {
            // fighter replacement flow:76-80 / 308-315 uses source motion 13 for a normal switch-in.
            // Its wireframe, clipping and sprite-effect stages are not decoded yet, but this
            // recovered motion runs through the motion machine rather than being invented.
            var motion = _battlePendingChangeMotion;
            _battlePendingChangeMotion = 0;
            if (!_battleRenderer.TrySetPlayerMotion(motion, restart: true))
                _log.Warn("battle-change-motion-unwired", $"fighter={playerId} motion={motion}");
        }
        _battleRenderedPlayerId = playerId;
        _battleRenderedEnemyId = enemyId;
        QuietLog.Info($"battle-model-replaced player={playerId} enemy={enemyId}");
    }

    /// <summary>On a victory, resolves the encounter's real reward, shares its exp by the partners
    /// who fought, applies the growth (exp + level-up via GrowthCalculator.AddExp) and builds the
    /// report view the HUD draws. Flight and defeat resolve no reward.</summary>
    private void ResolveBattleOutcome(BattleFlowController flow)
    {
        if (flow.Outcome != BattleOutcome.Won) return;
        var reward = ResolveReward(_lastBattleEncounter);
        var fought = flow.State.Fought.Count(foughtFlag => foughtFlag);
        var expShare = GrowthCalculator.ExpShare(reward.Exp, fought);
        var partners = new List<BattlePartnerReportView>();
        var reportPartners = new List<FightReportPartner>();
        for (var i = 0; i < flow.State.Players.Length; i++)
        {
            var fighter = flow.State.Players[i];
            if (fighter is null || fighter.Id == 0) continue;
            var gained = flow.State.Fought[i] ? expShare : 0;
            var before = _partyGrowth is not null && i < _partyGrowth.Count ? _partyGrowth[i] : null;
            var levelBefore = before?.Level ?? TestTeamGrowth.Level;
            var expBefore = before?.Exp ?? 0;
            var leveled = false;
            if (gained > 0 && _partyGrowth is not null && i < _partyGrowth.Count && _growthTables is not null)
            {
                var digimon = DigimonFor(i);
                var curve = new PartnerGrowthCurve(digimon.ExpRate, digimon.HpGrowth, digimon.MpGrowth, digimon.StatGrowth, digimon.ResistGrowth);
                leveled = GrowthCalculator.AddExp(_growthTables, _partyGrowth[i], curve, gained, _battleRng);
            }
            var level = _partyGrowth is not null && i < _partyGrowth.Count ? _partyGrowth[i].Level : TestTeamGrowth.Level;
            partners.Add(new BattlePartnerReportView(fighter.Id, NameLine(fighter.Id), gained, level, leveled));
            // The F8 fixture has no equipped Digimon slots. Do not synthesize their report data.
            reportPartners.Add(new FightReportPartner(i, i, fighter.Id, levelBefore, expBefore, gained, level, []));
        }
        _battleReport = new BattleReportView(BattleOutcome.Won, expShare, reward.Money, reward.DigimonExp, partners, BattleText());
        _fightReportInput = new FightReportInput(reportPartners, reward.Money, Item: 0);
        QuietLog.Info($"battle-reward encounter={_lastBattleEncounter} exp={reward.Exp} money={reward.Money} digimonExp={reward.DigimonExp} fought={fought} share={expShare} applied=[{string.Join(",", partners.Select(partner => $"fighter={partner.Id}:+{partner.ExpGained}"))}]");
    }

    private FightReportController BuildFightReportController(BattleFlowController flow)
    {
        if (flow.Outcome != BattleOutcome.Won || _fightReportInput is null)
            throw new InvalidOperationException("fight report requested without a resolved victory input");
        _log.Warn("report-partner-slots-unavailable", "report-partner-slots-unavailable");
        return new FightReportController(_fightReportInput);
    }

    private IReadOnlyDictionary<int, BattleTextLine> BuildReportNames() => _fightReportInput?.Partners
        .ToDictionary(partner => partner.FighterId, partner => NameLine(partner.FighterId))
        ?? [];

    /// <summary>The encounter's reward, or an explicit zero reward when the id is absent or
    /// out of the manifest (never invented).</summary>
    private BattleReward ResolveReward(int encounter)
    {
        if (_rewardsData is null || encounter < 0 || encounter >= _rewardsData.Rewards.Count)
            return new BattleReward(0, 0, 0);
        var entry = _rewardsData.Rewards[encounter];
        return new BattleReward(entry.DigimonExp, entry.Exp, entry.Money);
    }

    private Conversion.DigimonData DigimonFor(int slot)
    {
        if (_partyDigimonIds is null || slot < 0 || slot >= _partyDigimonIds.Count || _fighterData is null)
            throw new InvalidOperationException($"test team slot {slot} has no resolved digimon");
        return _fighterData.Digimon[_partyDigimonIds[slot]];
    }

    private void TickBattleMenu()
    {
        var flow = BattleFlow;
        if (flow.Phase != BattleFlowPhase.PlayerCommand) return;

        RefreshActivePartnerCatalogs();
        // the player-turn presentation flow:223-259 routes a KO through createSwitchMenu(canCancel=0), not the
        // six-command root. Keep that constraint at the host/input boundary.
        if (flow.State.ActivePlayerFighter?.IsKnockedOut == true && _battleMenu.Menu == BattleMenuKind.Root)
        {
            _battleSwitchForced = true;
            OpenSubmenu(BattleMenuKind.Switch, SwitchLines(flow), slot => !flow.State.Players[slot]!.IsKnockedOut);
        }

        // Press-edge detection: the cursor advances once per key press, never once per frame.
        var up = _input.State.HeldUp;
        var down = _input.State.HeldDown;
        var upEdge = up && !_battlePrevUp;
        var downEdge = down && !_battlePrevDown;
        _battlePrevUp = up;
        _battlePrevDown = down;
        if (upEdge) _battleMenu.MoveUp();
        if (downEdge) _battleMenu.MoveDown();
        if (_input.State.ConsumePreviousPagePressed() && _battleMenu.PreviousPage())
            QuietLog.Info($"battle-menu page first={_battleMenu.First} cursor={_battleMenu.Cursor}");
        if (_input.State.ConsumeNextPagePressed() && _battleMenu.NextPage())
            QuietLog.Info($"battle-menu page first={_battleMenu.First} cursor={_battleMenu.Cursor}");

        if (_input.State.ConsumeCancelPressed())
        {
            CancelBattleMenu(flow);
            return;
        }
        if (!_input.State.ConsumeActionPressed()) return;
        if (!_battleMenu.CanConfirm) return;

        switch (_battleMenu.Menu)
        {
            case BattleMenuKind.Root:
                ConfirmRoot(flow);
                break;
            case BattleMenuKind.Technique:
                if (_battleSubmenuArgs.Count > _battleMenu.SelectedIndex) flow.Choose(BattleCommand.Technique, _battleSubmenuArgs[_battleMenu.SelectedIndex], _battleRng);
                break;
            case BattleMenuKind.Digivolve:
                if (_battleSubmenuArgs.Count > _battleMenu.Cursor) flow.Choose(BattleCommand.Digivolve, _battleSubmenuArgs[_battleMenu.Cursor], _battleRng);
                break;
            case BattleMenuKind.Switch:
                if (_battleSubmenuArgs.Count > _battleMenu.Cursor) OpenSwitchInMenu(flow, _battleSubmenuArgs[_battleMenu.Cursor]);
                break;
            case BattleMenuKind.SwitchIn:
                if (_battleSubmenuArgs.Count > _battleMenu.Cursor) OpenPairSwitchMenu(flow, _battleSubmenuArgs[_battleMenu.Cursor]);
                break;
            case BattleMenuKind.PairSwitch:
                if (_battleMenu.Cursor == 0 && _battleSwitchPartner >= 0)
                {
                    if (flow.Choose(BattleCommand.Switch, _battleSwitchPartner, _battleRng))
                    {
                        _battlePendingChangeMotion = 13;
                        _battleSwitchForced = false;
                        QuietLog.Info($"battle-menu switch player={_battleSwitchPartner} action=5 consumes_turn=true");
                    }
                    else
                    {
                        var target = flow.State.Players[_battleSwitchPartner];
                        _log.Warn("battle-menu-switch-refused", $"slot={_battleSwitchPartner} active={flow.State.ActivePlayer} target={target?.Id} hp={target?.Hp}");
                    }
                }
                break;
            case BattleMenuKind.Item:
                if (_battleSubmenuArgs.Count > _battleMenu.SelectedIndex) flow.Choose(BattleCommand.Item, _battleSubmenuArgs[_battleMenu.SelectedIndex], _battleRng);
                break;
        }
    }

    private void ConfirmRoot(BattleFlowController flow)
    {
        switch (_battleMenu.Cursor)
        {
            case 0:
                flow.Choose(BattleCommand.Attack, 0, _battleRng);
                // Source attack action drives the fighter control motion. Motion 3 is Kotemon's
                // decoded basic action and its terminal blend returns every idling model to motion 1.
                _battleRenderer?.SetPlayerMotion(3, restart: true);
                break;
            case 1:
                OpenSubmenu(BattleMenuKind.Technique, flow.PlayerTechniques.Select(technique => (technique.Id, technique.Text ?? BattleTextLine.Missing)).ToList(),
                    line: (id, text) =>
                    {
                        var technique = flow.PlayerTechniques.First(candidate => candidate.Id == id);
                        var fighter = flow.State.ActivePlayerFighter!;
                        var enabled = fighter.Temporary || fighter.Mp >= technique.Tech.Mp;
                        var catalog = BattleText();
                        return new BattleMenuLine(text.Debug, enabled, text, Icon: technique.Tech.Icon + 0x37, Cost: technique.Tech.Mp,
                            DescriptionText: enabled ? catalog.SkillInfo(id) : catalog.MenuEntry(0x54),
                            CostLabelText: catalog.MenuEntry(0xD));
                    }, statusText: TechniqueStatusText(flow.State.ActivePlayerFighter!));
                break;
            case 2:
                OpenSubmenu(BattleMenuKind.Digivolve, flow.PlayerDigivolutions.Select(id => (id, NameLine(id))).ToList(),
                    line: (id, text) => new BattleMenuLine(text.Debug, id != flow.State.ActivePlayerFighter!.Id, text));
                break;
            case 3: OpenSubmenu(BattleMenuKind.Switch, SwitchLines(flow), slot => !flow.State.Players[slot]!.IsKnockedOut); break;
            case 4:
                OpenSubmenu(BattleMenuKind.Item, ItemLines(flow),
                    line: (id, text) => new BattleMenuLine(text.Debug, flow.ItemStock.TryGetValue(id, out var quantity) && quantity > 0, text,
                        Icon: BattleItemsData.Items.First(item => item.Id == id).Category,
                        Quantity: flow.ItemStock.TryGetValue(id, out quantity) ? quantity : 0,
                        DescriptionText: BattleText().ItemInfo(id)), statusText: new BattleMenuStatusText(BattleText().MenuEntry(0xC)));
                break;
            case 5: flow.Choose(BattleCommand.Flee, 0, _battleRng); break;
        }
    }

    private void OpenSubmenu(BattleMenuKind kind, IReadOnlyList<(int Arg, BattleTextLine Text)> entries, Func<int, bool>? enabled = null,
        Func<int, BattleTextLine, BattleMenuLine>? line = null, BattleMenuStatusText? statusText = null)
    {
        if (entries.Count == 0) return;  // nothing to pick: stay on the root menu
        _battleSubmenuArgs.Clear();
        var lines = new List<BattleMenuLine>(entries.Count);
        foreach (var (arg, text) in entries)
        {
            _battleSubmenuArgs.Add(arg);
            lines.Add(line?.Invoke(arg, text) ?? new BattleMenuLine(text.Debug, enabled?.Invoke(arg) ?? true, text));
        }
        var textCatalog = BattleText();
        _battleMenu.Open(kind, lines, previousPageText: kind is BattleMenuKind.Technique or BattleMenuKind.Item ? textCatalog.MenuEntry(0x11) : null,
            nextPageText: kind is BattleMenuKind.Technique or BattleMenuKind.Item ? textCatalog.MenuEntry(0x12) : null, statusText: statusText);
        QuietLog.Info($"battle-menu open={kind} lines={lines.Count}");
    }

    private void OpenSwitchInMenu(BattleFlowController flow, int partner)
    {
        var fighter = flow.State.Players[partner];
        if (fighter is null || fighter.Id == 0 || fighter.IsKnockedOut) return;
        _battleSwitchPartner = partner;
        // The remake has one recovered form per party member. It still passes through the
        // switch_in_the battle presentation flow first page rather than shortcutting into BattleFlow.Choose.
        OpenSubmenu(BattleMenuKind.SwitchIn, [(fighter.Id, NameLine(fighter.Id))]);
    }

    private void OpenPairSwitchMenu(BattleFlowController flow, int incomingId)
    {
        if (_battleSwitchPartner < 0 || flow.State.Players[_battleSwitchPartner]?.Id != incomingId) return;
        // FIGHTSTG_pairCursors[1] has one Changement line unless findPairTech found a pair
        // technique. Pair-tech data is deliberately not fabricated in this card.
        OpenSubmenu(BattleMenuKind.PairSwitch, [(0, BattleText().Command(3))]);
    }

    private void CancelBattleMenu(BattleFlowController flow)
    {
        switch (_battleMenu.Menu)
        {
            case BattleMenuKind.PairSwitch:
                OpenSwitchInMenu(flow, _battleSwitchPartner);
                break;
            case BattleMenuKind.SwitchIn:
                OpenSubmenu(BattleMenuKind.Switch, SwitchLines(flow), slot => !flow.State.Players[slot]!.IsKnockedOut);
                break;
            case BattleMenuKind.Switch:
                if (_battleSwitchForced)
                {
                    QuietLog.Info("battle-menu cancel=refused forced-switch");
                    break;
                }
                _battleMenu.OpenRoot(flow.RootMenuLines());
                QuietLog.Info("battle-menu cancel=root");
                break;
            case BattleMenuKind.Technique:
            case BattleMenuKind.Digivolve:
            case BattleMenuKind.Item:
                _battleMenu.OpenRoot(flow.RootMenuLines());
                QuietLog.Info("battle-menu cancel=root");
                break;
        }
    }

    private IReadOnlyList<(int, BattleTextLine)> SwitchLines(BattleFlowController flow)
    {
        var result = new List<(int, BattleTextLine)>();
        for (var i = 0; i < flow.State.Players.Length; i++)
        {
            var partner = flow.State.Players[i];
            if (partner is null || partner.Id == 0) continue;
            if (i == flow.State.ActivePlayer) continue;
            result.Add((i, NameLine(partner.Id)));
        }
        return result;
    }

    private IReadOnlyList<(int, BattleTextLine)> ItemLines(BattleFlowController flow)
    {
        var result = new List<(int, BattleTextLine)>();
        foreach (var (id, count) in flow.ItemStock)
            if (count > 0) result.Add((id, flow.ItemText.TryGetValue(id, out var text) ? text : BattleTextLine.Missing));
        return result;
    }

    private BattleTextCatalog BattleText() => new(_battleTextData ?? throw new InvalidOperationException("battle text unavailable"));

    private BattleMenuStatusText TechniqueStatusText(BattleFighter fighter)
    {
        var text = BattleText();
        var currentOverride = fighter.Temporary
            ? text.MenuEntry(fighter.Mp < 100 ? 0x1A : fighter.Mp < 1000 ? 0xF : 0x24)
            : null;
        return new BattleMenuStatusText(text.MenuEntry(0xD), currentOverride, text.MenuEntry(0x10));
    }

    private string NameOf(int id) => $"fighter-{id}";

    /// <summary>Refreshes the active partner's basic attack, technique menu and digivolution
    /// forms when the active fighter changes (start, a manual Switch, a Digivolve).</summary>
    private void RefreshActivePartnerCatalogs()
    {
        var flow = BattleFlow;
        var active = flow.State.ActivePlayerFighter;
        if (active is null) return;
        if (active.Id == _battleCatalogForId) return;
        _battleCatalogForId = active.Id;
        flow.PlayerBasicTech = BattleSetupFactory.PlayerBasicTech(FighterData, TechniqueData, active.Id);
        flow.PlayerTechniques = BattleSetupFactory.PlayerTechniques(FighterData, TechniqueData, active.Id, flow.Text ?? throw new InvalidOperationException("battle text unavailable"));
        var partnerIndex = BattleSetupFactory.DigimonIndexFor(FighterData, active.Id);
        flow.PlayerDigivolutions = partnerIndex >= 0
            ? BattleSetupFactory.DigivolutionForms(DigivolutionData, partnerIndex, TestTeamGrowth.Level)
            : [];
    }

    /// <summary>Injects the battle-item catalogue and a debug-only stock (three of each item,
    /// never a real save's bag) into the flow.</summary>
    private void WireBattleCatalogs(BattleFlowController flow)
    {
        var items = new Dictionary<int, BattleItem>();
        var stock = new Dictionary<int, int>();
        var itemText = new Dictionary<int, BattleTextLine>();
        if (_battleItemsData is not null)
        {
            foreach (var item in _battleItemsData.Items)
            {
                var battleItem = BattleSetupFactory.ToItem(item);
                items[battleItem.Id] = battleItem;
                stock[battleItem.Id] = 3;
                itemText[battleItem.Id] = BattleTextCatalog.Tokens(item.Name, $"FSITMNAM id={item.NameId}");
            }
        }
        flow.Items = items;
        flow.ItemStock = stock;
        flow.ItemText = itemText;
    }

    /// <summary>Builds source-backed DIGNAM display records for every party/enemy fighter.
    /// The renderer draws their token streams and the debug names remain provenance labels only.</summary>
    private Dictionary<int, int> BuildBattleNameIds(IReadOnlyList<int> party, FighterData fighters, EncounterData encounters, int encounterIndex)
    {
        var nameIds = new Dictionary<int, int>();
        foreach (var id in party)
            if (fighters.Digimon.TryGetValue(id, out var partner)) nameIds[id] = partner.NameId;
        var table = fighters.BattleTable.ToDictionary(entry => entry.Id);
        var encounter = encounters.Encounters.FirstOrDefault(candidate => candidate.Index == encounterIndex);
        if (encounter is not null)
            foreach (var fighter in encounter.Fighters.Where(fighter => fighter != 0))
                if (table.TryGetValue(fighter, out var enemy)) nameIds[fighter] = enemy.NameId;
        return nameIds;
    }

    /// <summary>DIGNAM identity is always the decoded record's nameId. The battle map covers
    /// active fighters, while the Digimon table also covers menu-only digivolution forms.</summary>
    private BattleTextLine NameLine(int fighterId)
    {
        if (_battleNameIds.TryGetValue(fighterId, out var nameId)) return BattleText().DigimonByNameId(nameId);
        if (_fighterData?.Digimon.TryGetValue(fighterId, out var digimon) == true)
            return BattleText().DigimonByNameId(digimon.NameId);
        var enemy = _fighterData?.BattleTable.FirstOrDefault(entry => entry.Id == fighterId);
        return enemy is null ? BattleTextLine.Missing : BattleText().DigimonByNameId(enemy.NameId);
    }

    private BattleView? BuildBattleView()
    {
        var flow = _battleFlow;
        if (flow is null) return null;
        var players = new List<BattleFighterView>();
        foreach (var fighter in flow.State.Players)
        {
            if (fighter is null) { players.Add(new BattleFighterView(0, null, 0, 0, 0, 0, false, false)); continue; }
            players.Add(new BattleFighterView(fighter.Id, NameLine(fighter.Id), fighter.Hp, fighter.MaxHp, fighter.Mp, fighter.MaxMp,
                flow.State.ActivePlayer >= 0 && flow.State.Players[flow.State.ActivePlayer] == fighter, fighter.IsKnockedOut));
        }
        var enemies = new List<BattleFighterView>();
        foreach (var fighter in flow.State.Enemies)
        {
            if (fighter is null) { enemies.Add(new BattleFighterView(0, null, 0, 0, 0, 0, false, false)); continue; }
            enemies.Add(new BattleFighterView(fighter.Id, NameLine(fighter.Id), fighter.Hp, fighter.MaxHp, fighter.Mp, fighter.MaxMp,
                flow.State.ActiveEnemy >= 0 && flow.State.Enemies[flow.State.ActiveEnemy] == fighter, fighter.IsKnockedOut));
        }
        var menu = flow.Phase == BattleFlowPhase.PlayerCommand ? _battleMenu.View : null;
        return new BattleView(flow.Phase, flow.Outcome, players, enemies, flow.Message, menu, _battleReport, flow.MessageText, flow.MessageVisible);
    }

    /// <summary>The decoded battle-model JSON for a fighter id from the validated data pack.
    /// <c>DW3_BATTLE_MODEL</c> overrides the player path for isolated probes.</summary>
    private string ResolveBattleModelPath(int fighterId) =>
        _options.BattleModel
        ?? BattleLoader.ModelPath(fighterId);

    /// <summary>The decoded fight-stage JSON for a stage id from the validated data pack.
    /// <c>DW3_BATTLE_STAGE</c> overrides the path for isolated probes.</summary>
    private string ResolveBattleStagePath(int stageId) =>
        _options.BattleStage
        ?? BattleLoader.StagePath(stageId);

    /// <summary>The fight stage (AreaBattle.stage) of an encounter, resolved from the encounter
    /// manifest: the first AreaBattle whose encounter matches. A field encounter reuses the field
    /// stage's own area battle, so this resolves the same stage the source battle would use.</summary>
    private int ResolveBattleStage(int encounterIndex)
    {
        if (_encounterData is null) return 18; // the F8 test encounter 200's stage, when the manifest is absent
        foreach (var stage in _encounterData.Stages.Values)
            foreach (var array in stage.StageBattles)
                foreach (var place in array.Places)
                    foreach (var area in place.Areas)
                        foreach (var battle in area.Battles)
                            if (battle.Encounter == encounterIndex)
                                return battle.Stage;
        return 18;
    }

    #endregion

    #region Field interaction and dialogue

    private sealed class SystemRandomEncounterRng : IEncounterRng
    {
        private readonly System.Random _random;

        public SystemRandomEncounterRng()
        {
            var seedRaw = System.Environment.GetEnvironmentVariable("DW3_RNG_SEED");
            _random = int.TryParse(seedRaw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var seed)
                ? new System.Random(seed)
                : new System.Random();
        }

        /// <summary>Returns the next encounter random value in the controller's 0..4095 domain.</summary>
        public int Draw() => _random.Next(0, 4096);
    }

    /// <summary>The battle's RNG, mirroring SystemRandomEncounterRng (same DW3_RNG_SEED seed).</summary>
    private sealed class SystemRandomBattleRng : IBattleRng
    {
        private readonly System.Random _random;

        public SystemRandomBattleRng()
        {
            var seedRaw = System.Environment.GetEnvironmentVariable("DW3_RNG_SEED");
            _random = int.TryParse(seedRaw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var seed)
                ? new System.Random(seed)
                : new System.Random();
        }

        /// <summary>Returns the next battle random value in the controller's 0..4095 domain.</summary>
        public int NextValue() => _random.Next(0, 4096);
    }

    #endregion

    #region Field interaction and dialogue

    // After a climb lands on its own slot foot, the actor's collision circle can still
    // overlap the wall one row above the (open) foot cell, leaving the player stuck in
    // every direction. Nudge back to the nearest free cell, mirroring the source's
    // diamond-only leading-edge probes which let the player walk straight down off it.
    private void DepenetrateFromWall()
    {
        if (_floor is null) return;
        var pos = _state.Position;
        if (CircleFreeAt(pos)) return;
        var radius = Locomotion.PlayerCollisionRadius;
        var limit = (int)MathF.Ceiling(radius) + 1;
        var dirs = new[] { (0, 1), (0, -1), (1, 0), (-1, 0), (1, 1), (1, -1), (-1, 1), (-1, -1) };
        for (var dist = 1; dist <= limit; dist++)
            foreach (var (dx, dy) in dirs)
            {
                var candidate = new NumericsVector2(pos.X + dx * dist, pos.Y + dy * dist);
                if (CircleFreeAt(candidate)) { _state.Position = candidate; return; }
            }
    }

    private bool CircleFreeAt(NumericsVector2 pos)
    {
        var map = Floor;
        var radius = Locomotion.PlayerCollisionRadius;
        if (pos.X - radius < 0f || pos.Y - radius < 0f || pos.X + radius > map.Width || pos.Y + radius > map.Height) return false;
        var minX = Math.Max(0, (int)MathF.Floor(pos.X - radius));
        var maxX = Math.Min(map.Width - 1, (int)MathF.Ceiling(pos.X + radius));
        var minY = Math.Max(0, (int)MathF.Floor(pos.Y - radius));
        var maxY = Math.Min(map.Height - 1, (int)MathF.Ceiling(pos.Y + radius));
        var radiusSq = radius * radius;
        for (var cy = minY; cy <= maxY; cy++)
            for (var cx = minX; cx <= maxX; cx++)
            {
                if (map.CellAt(new NumericsVector2(cx, cy)) != 0) continue;
                var closest = new NumericsVector2(Math.Clamp(pos.X, cx, cx + 1f), Math.Clamp(pos.Y, cy, cy + 1f));
                if (NumericsVector2.DistanceSquared(pos, closest) <= radiusSq) return false;
            }
        return true;
    }

    /// <summary>FIELDSTG_setOffTrigger's climb branch: SLOT_CLIMB_UP climbs with the raw
    /// trigger direction, SLOT_CLIMB_DOWN with dir==1 ? 5 : 3 (the trigger-slot contract).</summary>
    private void StartClimb(Climb climb, int triggerDirection)
    {
        if (climb.Type == 2)
        {
            _climb.StartClimbUp(_state, triggerDirection, climb.X, climb.Y, climb.Height);
            QuietLog.Info($"climb start type=up slot={climb.Slot} dir={triggerDirection} pos=({climb.X},{climb.Y}) height={climb.Height}");
        }
        else
        {
            var dir = triggerDirection == 1 ? 5 : 3;
            _climb.StartClimbDown(_state, dir, climb.X, climb.Y, climb.Height);
            QuietLog.Info($"climb start type=down slot={climb.Slot} dir={dir} pos=({climb.X},{climb.Y}) height={climb.Height}");
        }
    }

    private void StartDrop(Drop drop, int triggerDirection)
    {
        _drop.StartDrop(_state, triggerDirection, drop.Height);
        QuietLog.Info($"drop start slot={drop.Slot} dir={triggerDirection} pos=({_state.Position.X},{_state.Position.Y}) height={drop.Height}");
    }

    // FIELDSTG_dirSteps (the actor movement contract): the source pixel step in front of the actor for each source
    // direction (0=down, counter-clockwise). getFacingTile adds this to the actor's tile.
    private static readonly NumericsVector2[] SourceDirSteps =
    [
        new(0, 16), new(-11, 11), new(-16, 0), new(-11, -11),
        new(0, -16), new(11, -11), new(16, 0), new(11, 11),
    ];

    /// <summary>The source tile the player faces: tile + FIELDSTG_dirSteps[facing] (the actor movement contract:33-38).</summary>
    private static NumericsVector2 FacingTile(GameState state)
    {
        var tile = new NumericsVector2((int)MathF.Floor(state.Position.X), (int)MathF.Floor(state.Position.Y));
        return tile + SourceDirSteps[state.Facing];
    }

    /// <summary>FIELDSTG_findActorAt (the actor interaction contract:142-158): the first dialogue actor whose
    /// box holds the tile — halfWidth each side of x, half that above and below y.</summary>
    private static DialogueActor? FindDialogueActorAt(ZoneDialogue zone, NumericsVector2 pos)
    {
        foreach (var actor in zone.Actors)
        {
            if (actor.Width is not { } width) continue; // no source half-width: cannot match
            var size = width >> 1;
            if (pos.X >= actor.X - size && actor.X + size >= pos.X)
            {
                size >>= 1;
                if (pos.Y >= actor.Y - size && actor.Y + size >= pos.Y)
                    return actor;
            }
        }
        return null;
    }

    /// <summary>Starts the selected talk of the actor facing the player, when one is present.
    /// Returns true when a conversation began. Unsupported controls (5 buffer insert, 8 player
    /// name) are signalled once per command and never rendered. The player stands and faces the
    /// actor it talks to.</summary>
    private bool TryStartDialogue()
    {
        if (_dialogueData is null || _currentZone is null) return false;
        if (!_dialogueData.Zones.TryGetValue(CurrentZone.Zone.Id, out var zoneDialogue)) return false;
        var actor = FindDialogueActorAt(zoneDialogue, FacingTile(_state));
        if (actor is null) return false;
        var talk = actor.Talks.FirstOrDefault(candidate => candidate.Selected);
        if (talk is null) return false;
        if (!_dialogueData.Texts.TryGetValue(zoneDialogue.TalkFile, out var entries)) return false;
        if (talk.Entry < 0 || talk.Entry >= entries.Count)
        {
            _log.Warn($"dialogue-entry-out-of-bounds:{CurrentZone.Zone.Id}:{actor.Id}",
                $"dialogue entry out of bounds zone={CurrentZone.Zone.Id} actor={actor.Id} entry={talk.Entry}");
            return false;
        }
        var entry = entries[talk.Entry];
        foreach (var command in entry.Where(DialogueController.IsUnsupported).Select(token => token.Command).Distinct())
            _log.Warn($"dialogue-control-unsupported:{command}",
                $"dialogue-control-unsupported cmd={command} zone={CurrentZone.Zone.Id} actor={actor.Id}");
        _dialogue.Start(CurrentZone.Zone.Id, actor, talk, entry);
        _dialogueBoxWidth = MeasureDialogueWidth(_dialogue.FullText, _dialogueData.Font);
        if (_dialogue.SpeakerIsPlayer)
            _log.Warn($"dialogue-player-name-unrendered:{CurrentZone.Zone.Id}:{actor.Id}",
                $"dialogue-player-name-unrendered zone={CurrentZone.Zone.Id} actor={actor.Id} (name entry not decoded, M4 scope)");
        _state.AnimationSet = 1;
        _state.AnimationDirection = _state.Facing;
        return true;
    }

    /// <summary>Builds the render view for the active conversation: the box origin and type are
    /// computed from the actor's source position, the camera and its facing, exactly like
    /// FIELDSTG_createTalk + FIELDSTG_getSpeechPos.</summary>
    private DialogueView BuildDialogueView()
    {
        var actor = _dialogue.Actor ?? throw new InvalidOperationException("dialogue view without actor");
        var (boxOrigin, type) = ComputeSpeechPosition(actor);
        return new DialogueView(
            boxOrigin,
            type,
            _dialogueBoxWidth,
            _dialogue.SpeakerName,
            _dialogue.SpeakerIsPlayer,
            _dialogue.CurrentPage,
            _dialogue.RevealCount,
            _dialogue.WaitingForAdvance);
    }

    /// <summary>FIELDSTG_toScreenPos + FIELDSTG_createTalk + FIELDSTG_getSpeechPos: the talk box
    /// origin in source PSX screen pixels and its type (0..3). The scroll is the camera centre
    /// minus half the 320x240 source screen.</summary>
    private (Vector2 BoxOrigin, int Type) ComputeSpeechPosition(DialogueActor actor)
    {
        var camera = CameraController.PositionFor(CurrentZone.Zone.Width, CurrentZone.Zone.Height, new Vector2(_state.Position.X, _state.Position.Y));
        var srcX = actor.X - camera.X + 160f;
        var srcY = actor.Y - camera.Y + 120f;
        // FIELDSTG_createTalk: the side depends on the facing and the source screen column.
        int type;
        switch (actor.Dir)
        {
            case 3 or 4 or 5:
                type = srcX >= 0xA0 ? 1 : 3;
                break;
            default: // 0,1,2,6,7
                type = srcX >= 0xA0 ? 0 : 2;
                break;
        }
        // FIELDSTG_createTalk edge adjustments: flip to the other vertical side near a screen edge.
        switch (type)
        {
            case 0: if (srcY < 0x79) type = 1; break;
            case 2: if (srcY < 0x79) type = 3; break;
            case 1: if (srcY >= 0xAC) type = 0; break;
            case 3: if (srcY >= 0xAC) type = 2; break;
        }
        // FIELDSTG_getSpeechPos: the box origin is the actor's screen position plus the type offset.
        var boxX = srcX + (type is 2 or 3 ? -0xB : 0xB);
        var boxY = srcY + (type is 0 or 2 ? -0x13 : -7);
        return (new Vector2(boxX, boxY), type);
    }

    /// <summary>createTalkBox's measure: the widest line of the full text (explicit newlines only),
    /// clamped to [0x5F, 0x8B] source pixels. The box width is fixed for the whole conversation.</summary>
    private static int MeasureDialogueWidth(IReadOnlyList<TextToken> tokens, FontData font)
    {
        var maxWidth = 0;
        var cursor = 0;
        foreach (var token in tokens)
        {
            if (token.IsControl)
            {
                if (token.Command == 1) cursor = 0;
                continue;
            }
            GlyphMetric? metric = token.IsGlyph
                ? (font.Glyphs.TryGetValue(token.Code, out var glyphMetric) ? glyphMetric : null)
                : (font.Icons.TryGetValue(token.Code, out var iconMetric) ? iconMetric : null);
            if (metric is not { } m) continue;
            cursor += m.Advance + m.Dx;
            if (cursor > maxWidth) maxWidth = cursor;
        }
        return Math.Clamp(maxWidth, 0x5F, 0x8B);
    }

    /// <summary>True when <paramref name="actor"/> has a selected talk whose entry splits into at
    /// least two pages (so a conversation can actually advance).</summary>
    private static bool HasMultiPageTalk(DialogueData dialogue, ZoneDialogue zone, DialogueActor actor)
    {
        var talk = actor.Talks.FirstOrDefault(candidate => candidate.Selected);
        if (talk is null) return false;
        if (!dialogue.Texts.TryGetValue(zone.TalkFile, out var entries)) return false;
        if (talk.Entry < 0 || talk.Entry >= entries.Count) return false;
        return DialogueController.SplitPages(entries[talk.Entry]).Count >= 2;
    }

    /// <summary>Places the player one source step behind an actor, facing it, so its facing tile
    /// is exactly the actor's tile.</summary>
    private static (NumericsVector2 Position, int Facing) FaceActor(DialogueActor actor)
    {
        const int facing = 6; // source right: dirSteps[6] = (16, 0)
        return (new NumericsVector2(actor.X - 16, actor.Y), facing);
    }

    /// <summary>The source PSX screen (320x240) to viewport (1280x720) mapping: S = 3*s + (160, 0).</summary>
    private static Vector2 SourceToViewport(Vector2 source) => new(source.X * 3f + 160f, source.Y * 3f);

    /// <summary>Counts pixels inside <paramref name="region"/> whose colour changed between the
    /// no-dialogue capture and the bubble capture.</summary>
    private static int CountChanged(Image before, Image after, Rect2 region)
    {
        var count = 0;
        var minX = Math.Max(0, (int)MathF.Floor(region.Position.X));
        var maxX = Math.Min(after.GetWidth() - 1, (int)MathF.Ceiling(region.End.X));
        var minY = Math.Max(0, (int)MathF.Floor(region.Position.Y));
        var maxY = Math.Min(after.GetHeight() - 1, (int)MathF.Ceiling(region.End.Y));
        for (var y = minY; y <= maxY; y++)
            for (var x = minX; x <= maxX; x++)
                if (ProbePixels.DifferentRgb(before.GetPixel(x, y), after.GetPixel(x, y))) count++;
        return count;
    }

    /// <summary>Counts the frame's opaque source texels whose viewport pixel changed between the
    /// two captures. Only opaque texels prove a real sprite: a flat DrawRect would light up every
    /// pixel including the frame's transparent gaps, which this deliberately does not count.</summary>
    private static int CountSpriteOpaqueChanged(Image before, Image after, Texture2D frame, Vector2 viewportTopLeft)
    {
        var image = frame.GetImage();
        if (image is null || image.IsEmpty()) return 0;
        var count = 0;
        for (var y = 0; y < image.GetHeight(); y++)
            for (var x = 0; x < image.GetWidth(); x++)
            {
                if (image.GetPixel(x, y).A < 0.99f) continue;
                var sx = (int)viewportTopLeft.X + x * 3 + 1;
                var sy = (int)viewportTopLeft.Y + y * 3 + 1;
                if (sx < 0 || sy < 0 || sx >= after.GetWidth() || sy >= after.GetHeight()) continue;
                if (ProbePixels.DifferentRgb(before.GetPixel(sx, sy), after.GetPixel(sx, sy))) count++;
            }
        return count;
    }

    /// <summary>Counts pixels inside <paramref name="region"/> that satisfy
    /// <paramref name="predicate"/>. Bounds are clamped to the image dimensions.
    /// Shared by CountDarkBluePixels, CountLightBluePixels, and CountBodyBluePixels
    /// to avoid repeating the identical clamp-and-iterate structure.</summary>
    private static int CountRegionPixels(Image image, Rect2 region, Func<Color, bool> predicate)
    {
        var count = 0;
        var minX = Math.Max(0, (int)MathF.Floor(region.Position.X));
        var maxX = Math.Min(image.GetWidth() - 1, (int)MathF.Ceiling(region.End.X));
        var minY = Math.Max(0, (int)MathF.Floor(region.Position.Y));
        var maxY = Math.Min(image.GetHeight() - 1, (int)MathF.Ceiling(region.End.Y));
        for (var y = minY; y <= maxY; y++)
            for (var x = minX; x <= maxX; x++)
            {
                if (predicate(image.GetPixel(x, y))) count++;
            }
        return count;
    }

    /// <summary>Counts viewport pixels inside <paramref name="region"/> that are dark blue
    /// (B above both R and G), i.e. the talk box's PALETTE_DARK_BLUE glyph ink. White or grey
    /// pixels (R==G==B) never qualify, so this proves the text renders dark blue, not white.</summary>
    private static int CountDarkBluePixels(Image image, Rect2 region) =>
        CountRegionPixels(image, region, c => c.B > c.R + 0.12f && c.B > c.G + 0.12f);

    /// <summary>Counts viewport pixels inside <paramref name="region"/> that are light blue: the
    /// blue channel above both others AND bright enough to exclude the dark-blue glyph ink (the
    /// panel is (222,238,255) half-blended over the scene, so its blend stays blue-dominant and
    /// bright; the dark-blue text is blue-dominant but dark). Grey (R==G==B) never qualifies.</summary>
    private static int CountLightBluePixels(Image image, Rect2 region) =>
        CountRegionPixels(image, region, c => c.B > c.R && c.B > c.G && c.R + c.G + c.B > 1.35f);

    /// <summary>Counts viewport pixels close to the panel's opaque light-blue body (222,238,255):
    /// bright and blue-dominant. A 50% blend over the scene stays too dark to qualify, so this
    /// proves the panel body is opaque (covering the decor), not half-blended.</summary>
    private static int CountBodyBluePixels(Image image, Rect2 region) =>
        CountRegionPixels(image, region, c => c.R > 0.8f && c.G > 0.85f && c.B > 0.9f && c.B > c.R);

    /// <summary>Counts a frame's opaque source texels whose rendered pixel is closer to the
    /// half-blend of the texel colour with the pre-dialogue scene than to the pure texel colour.
    /// A semi-transparent draw (setSemiTrans) produces the blend; an opaque draw reproduces the
    /// texel exactly, so this distinguishes the two.</summary>
    private static int CountSpriteSemiBlended(Image before, Image after, Texture2D frame, Vector2 viewportTopLeft)
    {
        var image = frame.GetImage();
        if (image is null || image.IsEmpty()) return 0;
        var count = 0;
        for (var y = 0; y < image.GetHeight(); y++)
            for (var x = 0; x < image.GetWidth(); x++)
            {
                var texel = image.GetPixel(x, y);
                if (texel.A < 0.99f) continue;
                var sx = (int)viewportTopLeft.X + x * 3 + 1;
                var sy = (int)viewportTopLeft.Y + y * 3 + 1;
                if (sx < 0 || sy < 0 || sx >= after.GetWidth() || sy >= after.GetHeight()) continue;
                var rendered = after.GetPixel(sx, sy);
                var back = before.GetPixel(sx, sy);
                var blended = new Color(
                    texel.R * 0.5f + back.R * 0.5f,
                    texel.G * 0.5f + back.G * 0.5f,
                    texel.B * 0.5f + back.B * 0.5f);
                if (ColorDistance(rendered, blended) < ColorDistance(rendered, texel)) count++;
            }
        return count;
    }

    /// <summary>Counts a frame's opaque source texels whose rendered pixel is closer to the pure
    /// texel colour than to the half-blend with the pre-dialogue scene. An opaque draw (only the
    /// STP texels blend) reproduces the texel exactly, so this is the complement of
    /// <see cref="CountSpriteSemiBlended"/> and proves the edge is opaque, not semi.</summary>
    private static int CountSpriteOpaquePure(Image before, Image after, Texture2D frame, Vector2 viewportTopLeft)
    {
        var image = frame.GetImage();
        if (image is null || image.IsEmpty()) return 0;
        var count = 0;
        for (var y = 0; y < image.GetHeight(); y++)
            for (var x = 0; x < image.GetWidth(); x++)
            {
                var texel = image.GetPixel(x, y);
                if (texel.A < 0.99f) continue;
                var sx = (int)viewportTopLeft.X + x * 3 + 1;
                var sy = (int)viewportTopLeft.Y + y * 3 + 1;
                if (sx < 0 || sy < 0 || sx >= after.GetWidth() || sy >= after.GetHeight()) continue;
                var rendered = after.GetPixel(sx, sy);
                var back = before.GetPixel(sx, sy);
                var blended = new Color(
                    texel.R * 0.5f + back.R * 0.5f,
                    texel.G * 0.5f + back.G * 0.5f,
                    texel.B * 0.5f + back.B * 0.5f);
                if (ColorDistance(rendered, texel) < ColorDistance(rendered, blended)) count++;
            }
        return count;
    }

    private static float ColorDistance(Color left, Color right) =>
        MathF.Abs(left.R - right.R) + MathF.Abs(left.G - right.G) + MathF.Abs(left.B - right.B);

    /// <summary>Verifies the bubble's corner sprite, panel, arrow and name window all produced real
    /// framebuffer pixels, and that the bubble is anchored above the actor's head rather than at
    /// the bottom of the screen.</summary>
    private void AssertDialogueBubble(Image before, Image after, DialogueActor actor)
    {
        var frames = _dialogueFrames ?? throw new InvalidOperationException("frame data unavailable");
        var textures = _frameTextures ?? throw new InvalidOperationException("frame textures unavailable");
        var (boxOrigin, type) = ComputeSpeechPosition(actor);
        var layout = frames.Layouts[type];
        var width = _dialogueBoxWidth;
        var widthOffset = type < 2 ? width : 0;

        var panelChanged = CountChanged(before, after, DialogueRenderer.BoxScreenRect);
        Require(panelChanged > 400, $"panel rendered too few changed pixels panel_changed={panelChanged}");

        // The panel must be the original light blue (B > R, bright), not the old grey: the
        // body colour is the disc's (222,238,255) half-blended, so blue-dominant bright pixels
        // dominate the rect while the dark-blue glyph ink (blue but dark) is excluded.
        var panelBlue = CountLightBluePixels(after, DialogueRenderer.BoxScreenRect);
        Require(panelBlue > 400, $"panel rendered too few light-blue pixels panel_blue={panelBlue} rect={DialogueRenderer.BoxScreenRect}");
        QuietLog.Info($"dialogue panel PASS light_blue={panelBlue} rect={DialogueRenderer.BoxScreenRect}");

        // The panel body must be opaque light blue across the middle of the box (the 12-wide
        // texture stretched to w), not a half-blended flat colour: only an opaque body reaches
        // the source body's brightness, a 50% blend over the scene does not.
        var panelRect = DialogueRenderer.BoxScreenRect;
        var middleRect = new Rect2(
            new Vector2(panelRect.Position.X + panelRect.Size.X * 0.25f, panelRect.Position.Y + panelRect.Size.Y * 0.15f),
            new Vector2(panelRect.Size.X * 0.5f, panelRect.Size.Y * 0.7f));
        var bodyBlue = CountBodyBluePixels(after, middleRect);
        Require(bodyBlue > 4000, $"panel middle not covered by the opaque body body_blue={bodyBlue} rect={middleRect}");

        // The panel texture keeps its vertical gradient and the two border bands (not flattened):
        // the decoded 12x62 texture's top line and bottom STP rows differ from the light-blue body.
        var panelImage = PanelTexture.GetImage() ?? throw new InvalidOperationException("panel texture image unavailable");
        Require(!panelImage.IsEmpty(), "panel texture image unavailable");
        Require(ProbePixels.DifferentRgb(panelImage.GetPixel(0, 30), panelImage.GetPixel(0, 13)),
            $"panel texture flattened at the top line body={panelImage.GetPixel(0, 30)} top={panelImage.GetPixel(0, 13)}");
        Require(ProbePixels.DifferentRgb(panelImage.GetPixel(0, 30), panelImage.GetPixel(0, 60)),
            $"panel texture flattened at the bottom line body={panelImage.GetPixel(0, 30)} bottom={panelImage.GetPixel(0, 60)}");

        // The two edge sprites are drawn opaque: setSemiTrans on a textured primitive blends only
        // the STP-marked texels (already baked into the decoded PNG alpha-128), so the edge's
        // opaque texels render as the pure frame colour, never a half-blend with the scene. The
        // left edge sits over the scene, so it is the clean probe: almost all its opaque texels
        // must render pure.
        var leftTopLeft = SourceToViewport(boxOrigin + layout.Sprite0Pos - new Vector2(widthOffset, 0));
        var leftPure = CountSpriteOpaquePure(before, after, textures[0], leftTopLeft);
        var leftSemi = CountSpriteSemiBlended(before, after, textures[0], leftTopLeft);
        Require(leftPure > 400, $"left edge rendered too few opaque texels left_pure={leftPure} left_semi={leftSemi}");
        Require(leftSemi < leftPure, $"left edge rendered semi-transparent left_semi={leftSemi} >= left_pure={leftPure}");
        QuietLog.Info($"dialogue 9-patch PASS body_blue={bodyBlue} left_pure={leftPure} left_semi={leftSemi} rect={panelRect}");

        var cornerTopLeft = SourceToViewport(boxOrigin + layout.SpritePos);
        var cornerChanged = CountSpriteOpaqueChanged(before, after, textures[layout.Sprite], cornerTopLeft);
        Require(cornerChanged > 15, $"corner sprite rendered too few opaque pixels corner_changed={cornerChanged}");

        var arrowX = type is 2 or 3 ? width - 14 : layout.Arrow.X;
        var arrowTopLeft = SourceToViewport(boxOrigin + new Vector2(arrowX, layout.Arrow.Y));
        var arrowChanged = CountSpriteOpaqueChanged(before, after, textures[7], arrowTopLeft);
        Require(arrowChanged > 6, $"arrow rendered too few opaque pixels arrow_changed={arrowChanged}");

        var nameOrigin = SourceToViewport(new Vector2(boxOrigin.X + layout.NameX - widthOffset, boxOrigin.Y + layout.NameY));
        var nameChanged = CountChanged(before, after, new Rect2(nameOrigin, new Vector2(width * 3f, 13f * 3f)));
        Require(nameChanged > 6, $"name window rendered too few pixels name_changed={nameChanged}");

        // The text (and name) ink must be the original dark blue (PALETTE_DARK_BLUE),
        // never white: count blue-dominant glyph pixels over the panel the renderer
        // laid out (both windows receive setPalette(PALETTE_DARK_BLUE), and the panel
        // is grey, so blue-dominant pixels inside it are the glyph ink, not the scene).
        var textBlue = CountDarkBluePixels(after, DialogueRenderer.BoxScreenRect);
        Require(textBlue > 40, $"dialogue text rendered too few dark-blue glyph pixels text_blue={textBlue} rect={DialogueRenderer.BoxScreenRect}");

        var camera = CameraController.PositionFor(CurrentZone.Zone.Width, CurrentZone.Zone.Height, new Vector2(_state.Position.X, _state.Position.Y));
        var actorScreenY = (actor.Y - camera.Y) * 3f + 360f;
        var rect = DialogueRenderer.BoxScreenRect;
        // The bubble is anchored just above the actor's head (types 0/2) or just below its feet
        // (types 1/3, the source's edge flip). A distance far above ~150 px would mean the old
        // fixed bottom panel, not a bubble that follows the speaker.
        var distanceToActor = type is 0 or 2 ? actorScreenY - rect.End.Y : rect.Position.Y - actorScreenY;
        Require(distanceToActor >= 0f && distanceToActor < 150f,
            $"bubble is not anchored to the actor distance={distanceToActor:0} actor_y={actorScreenY:0} type={type} rect={rect}");
    }

    /// <summary>
    /// M4 dialogue acceptance: on S200, face a real dialogue actor and start a conversation with
    /// Cross, reveal the page (source showPage), verify the PSX bubble's corner sprite, panel,
    /// arrow and name window with real framebuffer pixels and its position at the actor, advance
    /// page by page, close, resume locomotion, verify the held-Cross anti-reopen, and verify the
    /// effect-not-applied signal on an effect actor. Uses the test driver's direct reposition.
    /// </summary>
    private async void RunDialogueProbe()
    {
        try
        {
            var dialogue = _dialogueData ?? throw new InvalidOperationException("dialogue data unavailable");
            if (!dialogue.Zones.TryGetValue("S200", out var zone))
                throw new InvalidOperationException("S200 has no dialogue zone");
            var simpleActor = zone.Actors.FirstOrDefault(actor =>
                HasMultiPageTalk(dialogue, zone, actor)
                && HasNonPlayerSpeakerName(dialogue, zone, actor)
                && FindDialogueActorAt(zone, new NumericsVector2(actor.X, actor.Y))?.Id == actor.Id)
                ?? throw new InvalidOperationException("S200 has no reachable named multi-page selected talk");
            var effectActor = zone.Actors.FirstOrDefault(actor => actor.Talks.Any(talk => talk.Selected && talk.Kind == "effect"))
                ?? throw new InvalidOperationException("S200 has no effect actor");
            var generatedRoot = _options.ReferenceDir ?? _dataRoot;
            var captureRoot = ProbeOutputRoot(generatedRoot);

            // --- simple conversation: open, reveal, render the bubble, advance, close, resume ---
            var (startPos, startFacing) = FaceActor(simpleActor);
            _state.Position = startPos;
            _state.SetFacing(startFacing);
            _input.State.ReleaseAction();
            Renderer.Present(_state, CurrentZone, f3On: false);
            DialogueRenderer.Present(null);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            var before = GetViewport().GetTexture().GetImage();
            Require(before.SavePng(Path.Combine(captureRoot, "dialogue-before.png")) == Error.Ok, "dialogue before capture failed");

            _input.State.PressAction();
            TickTestSimulation(); // Cross edge -> TryStartDialogue -> Start
            Require(_dialogue.IsActive, "Cross did not start the simple conversation");
            _input.State.ReleaseAction();
            TickTestSimulation(); // Opening -> Open (typewriter starts, reveal=0)
            Require(_dialogue.State == DialogueState.Open, $"dialogue did not reach Open state={_dialogue.State}");
            Require(_dialogue.PageCount >= 2, $"simple talk has too few pages to advance pages={_dialogue.PageCount}");
            Require(_dialogue.SpeakerName.Count > 0 && !_dialogue.SpeakerIsPlayer, "named talk lost its speaker name");

            // Reveal the page fully (source showPage) so the whole bubble is verifiable.
            _input.State.PressAction();
            TickTestSimulation();
            _input.State.ReleaseAction();
            TickTestSimulation();
            Require(_dialogue.RevealComplete, "Cross did not reveal the page (showPage)");

            DialogueRenderer.SetProbeArrowBrightness(0);
            Renderer.Present(_state, CurrentZone, f3On: false);
            DialogueRenderer.Present(BuildDialogueView());
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            var openCapture = GetViewport().GetTexture().GetImage();
            Require(openCapture.SavePng(Path.Combine(captureRoot, "dialogue-open.png")) == Error.Ok, "dialogue open capture failed");

            Require(DialogueRenderer.HasFramePixels, "bubble laid out no frame sprites");
            Require(DialogueRenderer.HasNamePixels, "bubble laid out no name glyphs");
            Require(DialogueRenderer.HasArrowPixels, "bubble laid out no arrow");
            Require(DialogueRenderer.HasGlyphPixels, "bubble laid out no text glyphs");
            AssertDialogueBubble(before, openCapture, simpleActor);
            QuietLog.Info($"dialogue render PASS zone=S200 actor={simpleActor.Id} type={DialogueRenderer.BoxType} glyphs={DialogueRenderer.GlyphCount} icons={DialogueRenderer.IconCount} name={DialogueRenderer.NameGlyphCount} rect={DialogueRenderer.BoxScreenRect}");

            // Advance page by page (reveal each, then Cross) until the last page closes.
            var pages = 0;
            var pageCount = _dialogue.PageCount;
            while (_dialogue.IsActive)
            {
                if (AdvanceDialogueOnePage()) pages++;
            }
            Require(!_dialogue.IsActive && _dialogue.State == DialogueState.Idle, "dialogue did not close to Idle after the last page");
            Require(pages == pageCount - 1, $"dialogue advanced {pages} pages instead of {pageCount - 1}");
            QuietLog.Info($"dialogue advance PASS zone=S200 actor={simpleActor.Id} pages={pageCount} advances={pages}");

            // Resume: reposition to the known-walkable spawn and drive held input, proving the
            // conversation released locomotion (the source spawn right-step is the M1 route).
            _state.Position = new NumericsVector2(1000, 236);
            _state.SetFacing(1);
            ApplyScene(GetOrLoadZone("S200"));
            var resumeStart = _state.Position;
            SetTestHeldDirection(SemanticDirection.Right);
            var moved = false;
            for (var tick = 0; tick < 12 && !moved; tick++)
            {
                SimulateStep(_ => { });
                if (_state.Position != resumeStart) moved = true;
            }
            SetTestHeldDirection(SemanticDirection.None);
            Require(moved, "locomotion did not resume after the conversation");
            QuietLog.Info($"dialogue resume PASS zone=S200 actor={simpleActor.Id} moved={moved}");

            // --- anti-reopen: a held Cross through the close rearms, never reopens ---
            var (againPos, againFacing) = FaceActor(simpleActor);
            _state.Position = againPos;
            _state.SetFacing(againFacing);
            _input.State.ReleaseAction();
            _input.State.PressAction();
            TickTestSimulation();
            _input.State.ReleaseAction();
            TickTestSimulation(); // Opening -> Open
            for (var p = 0; p < _dialogue.PageCount - 1; p++)
                AdvanceDialogueOnePage();
            if (!_dialogue.RevealComplete)
            {
                _input.State.PressAction();
                TickTestSimulation();
                _input.State.ReleaseAction();
                TickTestSimulation();
            }
            Require(_dialogue.State == DialogueState.Open && _dialogue.RevealComplete && _dialogue.PageIndex == _dialogue.PageCount - 1,
                "anti-reopen setup did not reach the revealed last page");
            _input.State.PressAction(); // hold Cross through the close
            TickTestSimulation();       // Open -> Closing
            TickTestSimulation();       // Closing -> Rearm
            TickTestSimulation();       // Rearm, ActionHeld -> stays Rearm (no reopen)
            Require(_dialogue.State == DialogueState.Rearm, "held Cross reopened the dialogue instead of rearming");
            _input.State.ReleaseAction();
            TickTestSimulation(); // Rearm -> Idle
            Require(_dialogue.State == DialogueState.Idle, "dialogue did not rearm after the action release");
            QuietLog.Info($"dialogue rearm PASS zone=S200 actor={simpleActor.Id} held-cross-rearmed=true");

            // --- effect: closing an effect talk signals it once and applies nothing ---
            var (effectPos, effectFacing) = FaceActor(effectActor);
            _state.Position = effectPos;
            _state.SetFacing(effectFacing);
            _input.State.ReleaseAction();
            _input.State.PressAction();
            TickTestSimulation();
            Require(_dialogue.IsActive, "Cross did not start the effect conversation");
            _input.State.ReleaseAction();
            TickTestSimulation(); // Opening -> Open
            var progressBefore = _state.Progress;
            var guard = 0;
            while (_dialogue.IsActive && guard++ < 64)
                AdvanceDialogueOnePage();
            Require(!_dialogue.IsActive, "effect conversation did not close");
            Require(_log.Warned($"dialogue-effect-not-applied:{CurrentZone.Zone.Id}:{effectActor.Id}"),
                "effect conversation did not signal dialogue-effect-not-applied");
            Require(_state.Progress == progressBefore, "effect conversation changed story progress");
            var talk = _dialogue.Talk ?? throw new InvalidOperationException("effect conversation did not retain its talk data");
            QuietLog.Info($"dialogue effect PASS zone=S200 actor={effectActor.Id} families=[{string.Join(",", talk.Families)}] progress_unchanged=true");

            Require(!_input.F3On, "dialogue probe enabled F3");
            QuietLog.Info($"dialogue probe PASS zone=S200 simple_actor={simpleActor.Id} effect_actor={effectActor.Id} pages={pageCount} capture={captureRoot}/dialogue-open.png");
            GetTree().Quit(0);
        }
        catch (Exception error)
        {
            GD.PushError($"DIALOGUE_PROBE_FAIL: {error.Message}");
            GetTree().Quit(3);
        }
    }

    /// <summary>Reveals the current page (source showPage) if needed, then presses Cross to advance
    /// to the next page (or close on the last). Returns true when a page was advanced past.</summary>
    private bool AdvanceDialogueOnePage()
    {
        if (!_dialogue.RevealComplete)
        {
            _input.State.PressAction();
            TickTestSimulation();
            _input.State.ReleaseAction();
            TickTestSimulation();
        }
        _input.State.PressAction();
        TickTestSimulation(); // Open -> Advance or Closing
        var advanced = _dialogue.State == DialogueState.Advance;
        _input.State.ReleaseAction();
        TickTestSimulation(); // Advance -> Open, or Closing -> Rearm
        if (_dialogue.State == DialogueState.Rearm) TickTestSimulation(); // Rearm -> Idle
        return advanced;
    }

    /// <summary>True when the actor's selected talk starts with a real speaker name (control 7
    /// pair), not the player's own name (a lone control 9).</summary>
    private static bool HasNonPlayerSpeakerName(DialogueData dialogue, ZoneDialogue zone, DialogueActor actor)
    {
        var talk = actor.Talks.FirstOrDefault(candidate => candidate.Selected);
        if (talk is null || !dialogue.Texts.TryGetValue(zone.TalkFile, out var entries)) return false;
        if (talk.Entry < 0 || talk.Entry >= entries.Count) return false;
        var entry = entries[talk.Entry];
        var first = -1;
        var second = -1;
        for (var index = 0; index < entry.Count; index++)
        {
            if (entry[index].IsControl && entry[index].Command == 7)
            {
                if (first < 0) first = index;
                else { second = index; break; }
            }
        }
        if (first < 0 || second < 0) return false;
        var nameCount = second - first - 1;
        if (nameCount == 0) return false;
        return !(nameCount == 1 && entry[first + 1].IsControl && entry[first + 1].Command == 9);
    }

    private static SemanticDirection DirectionToward(NumericsVector2 from, NumericsVector2 to)
    {
        var horizontal = to.X - from.X;
        var vertical = to.Y - from.Y;
        var left = horizontal < -0.5f;
        var right = horizontal > 0.5f;
        var up = vertical < -0.5f;
        var down = vertical > 0.5f;
        return InputState.DirectionFrom(up, down, left, right);
    }

    private void SetTestHeldDirection(SemanticDirection direction)
    {
        foreach (var key in new[] { Key.Q, Key.D, Key.Z, Key.S })
            _input.Handle(new InputEventKey { Keycode = key, Pressed = false });
        foreach (var key in direction switch
        {
            SemanticDirection.Up => new[] { Key.Z },
            SemanticDirection.UpRight => [Key.Z, Key.D],
            SemanticDirection.Right => [Key.D],
            SemanticDirection.DownRight => [Key.S, Key.D],
            SemanticDirection.Down => [Key.S],
            SemanticDirection.DownLeft => [Key.S, Key.Q],
            SemanticDirection.Left => [Key.Q],
            SemanticDirection.UpLeft => [Key.Z, Key.Q],
            _ => [],
        })
            _input.Handle(new InputEventKey { Keycode = key, Pressed = true });
    }

    private async System.Threading.Tasks.Task CaptureStill(string captureRoot, string fileName)
    {
        _renderer?.Present(_state, CurrentZone, _input.F3On);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var capturePath = Path.Combine(captureRoot, fileName);
        Require(GetViewport().GetTexture().GetImage().SavePng(capturePath) == Error.Ok, $"capture write failed={capturePath}");
    }

    private void ToggleF3()
    {
        _input.Handle(new InputEventKey { PhysicalKeycode = Key.F3, Pressed = true });
        _input.Handle(new InputEventKey { PhysicalKeycode = Key.F3, Pressed = false });
    }

    #endregion

    #region Scene transitions and cache contracts

    private async void RunCanalWaterProbe()
    {
        try
        {
            // Named water region: S200 canal north bank near StageTile object 37.
            // Source binding: depth-11 (background) stream tile of S200PACK.BIN at world (1117,352).
            // PS1 the field-layer record contract i==2 applies FIELDSTG_state.spriteColor (stageColor 0x54,0x67,0x96)
            // to the depth-11 layer unconditionally.  Grey tile ~(106,106,106) after modulation:
            //   R=106*(84/128)≈70  G=106*(103/128)≈85  B=106*(150/128)≈124  → blue-dominant.
            // StageTile object 37 (cycle=1 depth=6 abr=3, STP≈(230,238,246)) adds ~(57,59,61)
            // via quarter-additive → composite ~(127,144,185).  Clearly blue, not grey/black.
            const int CanalWorldX = 1117, CanalWorldY = 352;

            // Walk from spawn (1000,236) south to (1000,280): walkable, positions camera so
            // canal world (1117,352) is in the lower-right quadrant of the viewport.
            var target = new NumericsVector2(1000, 280);
            _input.Handle(new InputEventKey { Keycode = Key.Shift, Pressed = true });
            for (var tick = 0; tick < 600; tick++)
            {
                if (NumericsVector2.Distance(_state.Position, target) < 2f) break;
                var moved = false;
                foreach (var direction in ProbeSteering.DirectionsToward(_state.Position, target))
                {
                    if (TryTestMove(direction)) { moved = true; break; }
                }
                if (!moved) break;
            }
            _input.Handle(new InputEventKey { Keycode = Key.Shift, Pressed = false });
            SetTestHeldDirection(SemanticDirection.None);

            Require(_state.ZoneId == "S200", $"canal probe left S200 at {_state.Position}");

            // Advance 7 source ticks so the water shimmer animation is running.
            for (var tick = 0; tick < 9; tick++) SimulateStep(_ => { });

            Renderer.Present(_state, CurrentZone, f3On: false);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

            var generatedRoot = _options.ReferenceDir ?? _dataRoot;
            var captureRoot = ProbeOutputRoot(generatedRoot);
            var capture = GetViewport().GetTexture().GetImage();
            Require(capture.SavePng(Path.Combine(captureRoot, "canal-water-s200.png")) == Error.Ok, "canal water capture write failed");

            // Map known canal world coord to screen pixel using the source camera formula.
            var camera = CameraController.PositionFor(CurrentZone.Zone.Width, CurrentZone.Zone.Height,
                new Vector2(_state.Position.X, _state.Position.Y));
            var screenX = Mathf.RoundToInt((CanalWorldX - camera.X) * 3f + 640f);
            var screenY = Mathf.RoundToInt((CanalWorldY - camera.Y) * 3f + 360f);

            Require(screenX >= 0 && screenX < 1280 && screenY >= 0 && screenY < 720,
                $"canal coord ({CanalWorldX},{CanalWorldY}) maps to screen ({screenX},{screenY}) outside viewport — " +
                $"player=({_state.Position.X:0},{_state.Position.Y:0}) camera=({camera.X:0},{camera.Y:0})");

            // Sample a 3×3 area of source pixels (each 3× on screen) around the canal coord.
            // Assertion: majority of sampled pixels are blue-dominant (B > R+25 AND B > G+15).
            // This fails before the stageColor fix (grey≈106,106,106) and passes after (≈70,85,124).
            var blueCount = 0;
            var sampleCount = 0;
            for (var dy = -1; dy <= 1; dy++)
                for (var dx = -1; dx <= 1; dx++)
                {
                    var px = screenX + dx * 3;
                    var py = screenY + dy * 3;
                    if (px < 0 || px >= 1280 || py < 0 || py >= 720) continue;
                    var pixel = capture.GetPixel(px, py);
                    sampleCount++;
                    if (pixel.B8 > pixel.R8 + 25 && pixel.B8 > pixel.G8 + 15) blueCount++;
                }
            Require(sampleCount > 0, $"canal region at screen ({screenX},{screenY}) yielded no valid pixels");
            Require(blueCount * 2 >= sampleCount,
                $"canal water not blue: blueCount={blueCount}/{sampleCount} at screen ({screenX},{screenY}) — " +
                $"player=({_state.Position.X:0},{_state.Position.Y:0}) camera=({camera.X:0},{camera.Y:0})");

            // Second capture after shimmer advance: time-separated animation evidence.
            for (var tick = 0; tick < 9; tick++) SimulateStep(_ => { });
            Renderer.Present(_state, CurrentZone, f3On: false);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            var capture2 = GetViewport().GetTexture().GetImage();
            Require(capture2.SavePng(Path.Combine(captureRoot, "canal-water-s200-after.png")) == Error.Ok, "canal water after-capture write failed");

            QuietLog.Info($"canal water PASS zone=S200 named_region=canal_north_bank " +
                $"world=({CanalWorldX},{CanalWorldY}) screen=({screenX},{screenY}) " +
                $"blue_dominant={blueCount}/{sampleCount} " +
                $"player=({_state.Position.X:0},{_state.Position.Y:0}) " +
                $"source=S200PACK-depth11-stageColor-0x54_0x67_0x96 " +
                $"captures={captureRoot}/canal-water-s200[-after].png");
            // Force GC before quit to avoid Mono finalization racing the engine shutdown.
            capture = null!; capture2 = null!;
            GC.Collect(); GC.WaitForPendingFinalizers();
            GetTree().Quit(0);
        }
        catch (Exception error)
        {
            GD.PushError($"CANAL_WATER_PROBE_FAIL: {error.Message}");
            GetTree().Quit(3);
        }
    }

    private async void RunWarpProbe()
    {
        try
        {
            RunWarpEdge("S200", 4, 3, "s200-to-s235");
            Require(_state.ZoneId == "S235" && _state.Position == new NumericsVector2(278, 218) && _state.Facing == 3, "S200 manifest arrival mismatch");
            await CaptureWarp("warp-s235-entry.png");
            RunWarpEdge("S235", 1, 7, "s235-to-s200");
            Require(_state.ZoneId == "S200" && _state.Position == new NumericsVector2(672, 378) && _state.Facing == 7, "S235 manifest arrival mismatch");
            RunWarpEdge("S200", 4, 3, "s200-to-s235-reentry");
            Require(_state.ZoneId == "S235" && _warp.CommitCount == 3, "re-entry did not commit exactly once per edge");
            await CaptureWarp("warp-s235-reentry.png");
            QuietLog.Info($"warp probe PASS commits={_warp.CommitCount} zone={_state.ZoneId} state={_warp.State}");
            GetTree().Quit(0);
        }
        catch (Exception error)
        {
            GD.PushError($"WARP_PROBE_FAIL: {error.Message}");
            GetTree().Quit(3);
        }
    }

    private void RunWarpEdge(string sourceZone, int slot, int facing, string label)
    {
        var current = _currentZone ?? throw new InvalidOperationException("probe has no current zone");
        Require(current.Zone.Id == sourceZone, $"probe expected source={sourceZone}");
        _state.Position = TriggerSlotLocator.FindCell(current, slot);
        _state.SetFacing(facing);
        _input.State.ReleaseAction();
        _warp.Tick(_state, _input.State, _currentZone, new WarpCallbacks(ResolveTarget, ApplyScene, QuietLog.Info, _log.Warn, StartClimb, StartDrop));
        Require(_warp.State == WarpState.Offer, $"{label} did not offer");
        _input.State.PressAction();
        for (var tick = 0; tick < 12 && _warp.State != WarpState.Rearm; tick++)
            _warp.Tick(_state, _input.State, CurrentZone, new WarpCallbacks(ResolveTarget, ApplyScene, QuietLog.Info, _log.Warn, StartClimb, StartDrop));
        Require(_warp.State == WarpState.Rearm, $"{label} did not reach rearm");
        _input.State.ReleaseAction();
        _warp.Tick(_state, _input.State, CurrentZone, new WarpCallbacks(ResolveTarget, ApplyScene, QuietLog.Info, _log.Warn, StartClimb, StartDrop));
        Require(_warp.State == WarpState.Idle, $"{label} did not rearm after release");
    }

    private async System.Threading.Tasks.Task CaptureWarp(string fileName)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var captureRoot = ProbeOutputRoot(_dataRoot);
        var capturePath = Path.Combine(captureRoot, fileName);
        Require(GetViewport().GetTexture().GetImage().SavePng(capturePath) == Error.Ok, $"warp capture failed={capturePath}");
        QuietLog.Info($"warp capture={capturePath}");
    }

    /// <summary>Returns a zone from the session cache, building it on first use.</summary>
    /// <param name="zoneId">Normalized zone id (<c>Snnn</c>).</param>
    /// <returns>The built zone.</returns>
    private FieldZone GetOrLoadZone(string zoneId) => ZoneCache.GetOrLoad(zoneId);

    private FieldZone ResolveTarget(string zoneId)
    {
        if (_options.WarpProbeMissingTarget)
            throw new CacheResourceException("CACHE_RESOURCE_UNAVAILABLE", $"zone={zoneId} forced unavailable for probe");
        return GetOrLoadZone(zoneId);
    }

    /// <summary>
    /// Commits a loaded field scene to the active session. It rebinds collision, encounter, and
    /// occupant state before rebuilding renderer nodes, resets the player icon at the current actor
    /// position, and presents the completed scene once before the next simulation tick.
    /// </summary>
    private void ApplyScene(FieldZone zone)
    {
        if (_playerData is null || _renderer is null) throw new InvalidOperationException("renderer resources unavailable");
        _currentZone = zone;
        _floor = zone.Floor;
        _areas = zone.Areas;
        _occupants = zone.Occupants;
        _encounter.SetBattles(ZoneBattleTable.For(_encounterData, zone.Zone));
        IReadOnlyList<NpcRecord>? npcRecords = null;
        if (_npc is not null && _npc.Zones.TryGetValue(zone.Zone.Id, out var records)) npcRecords = records;
        _renderer.LoadZone(zone.Zone, _playerData, ActionData, frame => ResolvePlayerFrame(frame), (set, frame) => ResolveActionFrame(set, frame), npcRecords);
        _icon.Reset(_state.Position);
        _renderer.Present(_state, zone, _input.F3On);
    }

    private string ResolvePlayerFrame(int frame)
    {
        return _fieldLoader?.PlayerFramePath(frame)
            ?? throw new InvalidOperationException("field data-pack loader unavailable before host ready");
    }

    private string ResolveActionFrame(int set, int frame)
    {
        var loader = _animationLoader ?? throw new InvalidOperationException("field animation loader unavailable before host ready");
        return set is >= 0x16 and <= 0x19 ? loader.DropFramePath(frame) : loader.ClimbFramePath(frame);
    }

    /// <summary>Returns the directory that probe captures should be written to, creating
    /// it if it does not yet exist. Prefers the explicit DW3_CAPTURE_DIR option; otherwise
    /// places a "captures" subdirectory under <paramref name="dataRoot"/>. Shared by all
    /// probe methods to avoid repeating the inline null-coalesce + CreateDirectory pattern.</summary>
    private string ProbeOutputRoot(string dataRoot)
    {
        var root = _options.CaptureDir ?? Path.Combine(dataRoot, "captures");
        Directory.CreateDirectory(root);
        return root;
    }

    private static void Require(bool condition, string message) => ProbeRequirement.Require(condition, message);

    #endregion

    #region Icon and storage diagnostic probes

    private sealed record IconPixelCount(int Matched, int Red, int Blue);

    /// <summary>
    /// M5 connection-indicator probe: the decoded red+blue icon renders above the player
    /// (never a fabricated frame), glides after the player instead of teleporting, settles
    /// back onto the player once movement stops, and its substate-0 animation advances
    /// (two distinct decoded frames observed). Uses the shared SimulateStep path.
    /// </summary>
    private async void RunIconProbe()
    {
        try
        {
            if (_iconData is null) throw new InvalidOperationException("icon data unavailable");
            var generatedRoot = _options.ReferenceDir ?? _dataRoot;
            var captureRoot = ProbeOutputRoot(generatedRoot);

            Require(_state.ZoneId == "S200" && _state.Position == new NumericsVector2(1000, 236),
                "icon probe must begin at the source S200 spawn");
            var startFrame = _icon.View;
            Require(_iconData.Frames.ContainsKey(startFrame.Frame), $"icon view frame 0x{startFrame.Frame:X} is not a decoded frame");

            var imageA = await CaptureIconFrame(captureRoot, "icon-a.png");
            var countA = CountIconPixels(imageA, _iconData.Frames[startFrame.Frame], startFrame.Position);
            Require(countA.Matched > 0, "no decoded icon pixel rendered above the player");
            Require(countA.Red > 0 && countA.Blue > 0,
                $"icon frame 0x{startFrame.Frame:X} did not render red+blue pixels above the player red={countA.Red} blue={countA.Blue}");

            // Move right several fixed steps: the icon glides toward the player (exponential
            // follow) rather than teleporting, so it trails behind while the player is in motion.
            var origin = _state.Position;
            SetTestHeldDirection(SemanticDirection.Right);
            for (var tick = 0; tick < 6; tick++) TickTestSimulation();
            SetTestHeldDirection(SemanticDirection.None);
            var playerDx = _state.Position.X - origin.X;
            var iconDx = _icon.View.Position.X - origin.X;
            Require(playerDx > 0, "player did not move right");
            Require(iconDx > 0 && iconDx < playerDx,
                $"icon did not trail the player smoothly icon_dx={iconDx:0.###} player_dx={playerDx:0.###}");

            // Once movement stops the icon settles back onto the player (exponential catch-up).
            var settled = false;
            for (var tick = 0; tick < 120 && !settled; tick++)
            {
                TickTestSimulation();
                settled = NumericsVector2.Distance(_icon.View.Position, _state.Position) < 0.5f;
            }
            Require(settled, "icon did not settle onto the player after movement stopped");

            // The substate-0 animation advances: at least two distinct decoded frames observed.
            var observed = new HashSet<int> { startFrame.Frame };
            for (var tick = 0; tick < 60 && observed.Count < 2; tick++)
            {
                TickTestSimulation();
                observed.Add(_icon.View.Frame);
            }
            Require(observed.Count >= 2,
                $"icon animation did not advance observed_frames={string.Join(",", observed.OrderBy(frame => frame).Select(frame => $"0x{frame:X}"))}");

            var imageB = await CaptureIconFrame(captureRoot, "icon-b.png");
            var countB = CountIconPixels(imageB, _iconData.Frames[_icon.View.Frame], _icon.View.Position);
            Require(countB.Red > 0 && countB.Blue > 0,
                $"settled icon frame 0x{_icon.View.Frame:X} did not render red+blue pixels red={countB.Red} blue={countB.Blue}");

            QuietLog.Info($"icon probe PASS frame_a=0x{startFrame.Frame:X} red={countA.Red} blue={countA.Blue} player_dx={playerDx:0.###} icon_dx={iconDx:0.###} settled={settled} frames={string.Join(",", observed.OrderBy(frame => frame).Select(frame => $"0x{frame:X}"))} captures={captureRoot}/icon-[a|b].png");
            GetTree().Quit(0);
        }
        catch (Exception error)
        {
            GD.PushError($"ICON_PROBE_FAIL: {error.Message}");
            GetTree().Quit(3);
        }
    }

    private async System.Threading.Tasks.Task<Image> CaptureIconFrame(string captureRoot, string fileName)
    {
        Renderer.Present(_state, CurrentZone, f3On: false, iconView: _icon.View);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var image = GetViewport().GetTexture().GetImage();
        var path = Path.Combine(captureRoot, fileName);
        Require(image.SavePng(path) == Error.Ok, $"icon capture write failed={path}");
        return image;
    }

    private IconPixelCount CountIconPixels(Image capture, PlayerIconFrame frame, NumericsVector2 iconAnchor)
    {
        var source = Image.LoadFromFile(frame.Path);
        if (source is null || source.IsEmpty()) throw new InvalidOperationException($"icon frame image unreadable: {frame.Path}");
        var camera = CameraController.PositionFor(CurrentZone.Zone.Width, CurrentZone.Zone.Height, new Vector2(_state.Position.X, _state.Position.Y));
        var left = Mathf.RoundToInt((iconAnchor.X + frame.OffsetX - camera.X) * 3f + 640f);
        var top = Mathf.RoundToInt((iconAnchor.Y + frame.OffsetY - camera.Y) * 3f + 360f);
        var matched = 0;
        var red = 0;
        var blue = 0;
        for (var y = 0; y < source.GetHeight(); y++)
            for (var x = 0; x < source.GetWidth(); x++)
            {
                var expected = source.GetPixel(x, y);
                if (expected.A < 0.99f) continue;
                var screenX = left + x * 3 + 1;
                var screenY = top + y * 3 + 1;
                if (screenX < 0 || screenY < 0 || screenX >= capture.GetWidth() || screenY >= capture.GetHeight()) continue;
                var actual = capture.GetPixel(screenX, screenY);
                if (!ProbePixels.SameRgb(actual, expected)) continue;
                matched++;
                if (expected.R8 > 150 && expected.G8 < 120 && expected.B8 < 120) red++;
                else if (expected.B8 > 150 && expected.R8 < 120) blue++;
            }
        return new IconPixelCount(matched, red, blue);
    }

    /// <summary>
    /// Probe activated by <c>DW3_SQLITE_PROBE=1</c>.  Creates a temporary database,
    /// writes one row, reads it back, logs the result, and exits.  Proves that the
    /// SQLite native library (e_sqlite3) loads and operates correctly inside the
    /// exported Godot binary, with no disc or cache data required.
    /// </summary>
    private void RunSqliteProbe()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"dw3_sqlite_probe_{Guid.NewGuid():N}.db");
        try
        {
            const string probeValue = "sqlite-probe-ok";

            using (var conn = new SqliteConnection($"Data Source={dbPath}"))
            {
                conn.Open();
                using var create = conn.CreateCommand();
                create.CommandText = "CREATE TABLE probe (key TEXT PRIMARY KEY, value TEXT NOT NULL)";
                create.ExecuteNonQuery();

                using var insert = conn.CreateCommand();
                insert.CommandText = "INSERT INTO probe (key, value) VALUES ('run', @v)";
                insert.Parameters.AddWithValue("@v", probeValue);
                insert.ExecuteNonQuery();
            }

            string readBack;
            using (var conn = new SqliteConnection($"Data Source={dbPath}"))
            {
                conn.Open();
                using var select = conn.CreateCommand();
                select.CommandText = "SELECT value FROM probe WHERE key = 'run'";
                readBack = (string)(select.ExecuteScalar() ?? throw new InvalidOperationException("probe row not found"));
            }

            if (readBack != probeValue)
                throw new InvalidOperationException($"sqlite probe mismatch: expected={probeValue} actual={readBack}");

            QuietLog.Info($"sqlite probe PASS value={readBack}");
            GetTree().Quit(0);
        }
        catch (Exception ex)
        {
            GD.PushError($"sqlite probe FAIL: {ex.Message}");
            GetTree().Quit(3);
        }
        finally
        {
            try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch { /* best-effort cleanup */ }
        }
    }

    private async void CaptureSpawn()
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var generatedRoot = _options.ReferenceDir ?? _dataRoot;
        var captureRoot = ProbeOutputRoot(generatedRoot);
        var capturePath = Path.Combine(captureRoot, "s200-spawn.png");
        var status = GetViewport().GetTexture().GetImage().SavePng(capturePath);
        if (status != Error.Ok)
        {
            GD.PushError($"CACHE_RESOURCE_UNAVAILABLE: capture write failed: {capturePath} ({status})");
            GetTree().Quit(3);
            return;
        }
        QuietLog.Info($"capture={capturePath}");
        if (_options.CaptureAndQuit) GetTree().Quit(0);
    }

    #endregion
}
