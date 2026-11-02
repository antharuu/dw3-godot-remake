using System.Diagnostics;
using Godot;
using Dw3.Data;
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
                Callable.From(() => _ = SpawnCapture.RunAsync(this, _options.CaptureAndQuit)).CallDeferred();
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
        var run = ResolveProbe(methodName);
        Callable.From(() => _ = run()).CallDeferred();
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

    /// <summary>Finds the probe registered under an entry name in the field, dialogue, battle and storage families.</summary>
    /// <param name="entryName">Entry name chosen by a diagnostic runner, such as <c>RunWarpProbe</c>.</param>
    /// <returns>The probe body, to be started after the current frame.</returns>
    /// <exception cref="InvalidOperationException">No family registers the name.</exception>
    private Func<Task> ResolveProbe(string entryName)
    {
        var fieldProbe = FieldProbeCatalog.Create(entryName, this) ?? DialogueProbeCatalog.Create(entryName, this);
        if (fieldProbe is not null)
        {
            return fieldProbe.RunAsync;
        }

        if (BattleProbeCatalog.Create(entryName, new BattleProbeContext(this)) is { } battleProbe)
        {
            return battleProbe.RunAsync;
        }

        if (entryName == "RunSqliteProbe")
        {
            return () =>
            {
                SqliteProbe.Run(code => GetTree().Quit(code));
                return Task.CompletedTask;
            };
        }

        throw new InvalidOperationException($"no diagnostic probe is registered as {entryName}");
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

    /// <summary>Starts the selected talk of the actor facing the player, when one is present.
    /// Returns true when a conversation began. Unsupported controls (5 buffer insert, 8 player
    /// name) are signalled once per command and never rendered. The player stands and faces the
    /// actor it talks to.</summary>
    private bool TryStartDialogue()
    {
        if (_dialogueData is null || _currentZone is null) return false;
        if (!_dialogueData.Zones.TryGetValue(CurrentZone.Zone.Id, out var zoneDialogue)) return false;
        var actor = DialogueActorLocator.FindAt(zoneDialogue, DialogueActorLocator.FacingTile(_state));
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
        _dialogueBoxWidth = DialogueBubbleLayout.MeasureWidth(_dialogue.FullText, _dialogueData.Font);
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
        var camera = CameraController.PositionFor(CurrentZone.Zone.Width, CurrentZone.Zone.Height, new Vector2(_state.Position.X, _state.Position.Y));
        var (boxOrigin, type) = DialogueBubbleLayout.Place(actor, camera);
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
}
