using System.Diagnostics;
using Dw3.Data;
using Dw3Production.Conversion;
using Dw3Production.Diagnostics;
using Dw3Production.Diagnostics.Field;
using Dw3Production.Input;
using Dw3Production.Logging;
using Dw3Production.Presentation;
using Dw3Production.Scene;
using Dw3Production.Simulation;
using Dw3Production.Startup;
using Godot;
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
public partial class GameHost : Node2D, IDiagnosticProbeHost, IBattleMenuDriverHost, IFieldTickHost, IBattleTickHost, IInteractiveFrameHost
{
    #region Composition state

    private const int DebugBattleEncounter = 200;
    private const int BattleReportFadeFrames = 10;

    private readonly GameState _state = new();
    private readonly SimClock _clock = new();
    private readonly InteractiveFrameCoordinator _frameCoordinator;
    private readonly BattleMenuCoordinator _battleMenuCoordinator;
    private readonly BattleSetupCoordinator _battleSetupCoordinator;
    private readonly FieldRuntimeCoordinator _fieldRuntimeCoordinator;
    private readonly BattlePresentationCoordinator _battlePresentationCoordinator;
    private readonly FirstLaunchCompletionCoordinator _firstLaunchCompletionCoordinator;
    private readonly DiagnosticProbeResolver _diagnosticProbeResolver;
    private readonly DialogueController _dialogue = new();
    private readonly EncounterController _encounter = new(new SystemRandomEncounterRng());
    private readonly BattleMenuController _battleMenu = new();
    private readonly IBattleRng _battleRng = new SystemRandomBattleRng();
    private readonly FieldEncounterStepper _encounterStepper = new();

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
    private EncounterData? _encounterData;
    private FighterData? _fighterData;
    private TechniqueData? _techData;
    private DigivolutionData? _digivolutionData;
    private BattleItemsData? _battleItemsData;
    private BattleTextData? _battleTextData;
    private AreaMap? _areas;
    private BattleSetup? _activeBattle;
    private BattleFlowController? _battleFlow;
    private BattleRenderer? _battleRenderer;
    private FightReportRenderer? _fightReportRenderer;
    private FightReportController? _fightReportController;
    private FightReportInput? _fightReportInput;
    private int _battleReportFadeTicks;
    private int _lastBattleEncounter = -1;
    private int[] _lastBattleEnemyIds = [];
    private Dictionary<int, int> _battleNameIds = [];

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

    /// <summary>Initializes a new instance of the <see cref="GameHost"/> class.</summary>
    public GameHost()
    {
        _frameCoordinator = new InteractiveFrameCoordinator(_clock);
        _battleMenuCoordinator = new BattleMenuCoordinator(this);
        _battleSetupCoordinator = new BattleSetupCoordinator(this);
        _fieldRuntimeCoordinator = new FieldRuntimeCoordinator(this);
        _battlePresentationCoordinator = new BattlePresentationCoordinator(this);
        _firstLaunchCompletionCoordinator = new FirstLaunchCompletionCoordinator(this);
        _diagnosticProbeResolver = new DiagnosticProbeResolver(this);
    }

    /// <inheritdoc/>
    bool IInteractiveFrameHost.IsTestDriverActive => _testDriverActive;

    /// <inheritdoc/>
    bool IInteractiveFrameHost.HasInputFocus => _input.HasFocus;

    /// <inheritdoc/>
    bool IInteractiveFrameHost.IsInTitle => _inTitle;

    /// <inheritdoc/>
    bool IInteractiveFrameHost.HasField => _currentZone is not null;

    /// <inheritdoc/>
    bool IInteractiveFrameHost.HasActiveBattle => _activeBattle is not null;

    // These accessors mark lifecycle boundaries explicitly. A caller that needs an
    // initialized runtime collaborator receives a useful failure instead of masking
    // a violated startup invariant with the null-forgiving operator.

    /// <summary>Gets the active field zone; throws when the field scene has not been applied yet.</summary>
    private FieldZone CurrentZone => _currentZone ?? throw new InvalidOperationException("field scene has not been applied");

    /// <summary>Gets the field renderer; throws when the renderer has not been initialized yet.</summary>
    private FieldRenderer Renderer => _renderer ?? throw new InvalidOperationException("field renderer has not been initialized");

    /// <summary>Gets the active battle flow controller; throws when no battle is in progress.</summary>
    private BattleFlowController BattleFlow => _battleFlow ?? throw new InvalidOperationException("battle flow has not been initialized");

    /// <summary>Gets the battle renderer; throws when the battle renderer has not been initialized yet.</summary>
    private BattleRenderer BattleRenderer => _battleRenderer ?? throw new InvalidOperationException("battle renderer has not been initialized");

    /// <summary>Gets the battle data-pack loader; throws when battle data has not been loaded yet.</summary>
    private BattleDataPackLoader BattleLoader => _battleLoader ?? throw new InvalidOperationException("battle data has not been initialized");

    /// <summary>Gets the decoded fighter table; throws when fighter data has not been loaded yet.</summary>
    private FighterData FighterData => _fighterData ?? throw new InvalidOperationException("fighter data has not been initialized");

    /// <summary>Gets the decoded technique table; throws when technique data has not been loaded yet.</summary>
    private TechniqueData TechniqueData => _techData ?? throw new InvalidOperationException("technique data has not been initialized");

    /// <summary>Gets the decoded digivolution table; throws when digivolution data has not been loaded yet.</summary>
    private DigivolutionData DigivolutionData => _digivolutionData ?? throw new InvalidOperationException("digivolution data has not been initialized");

    /// <summary>Gets the decoded battle-item table; throws when battle item data has not been loaded yet.</summary>
    private BattleItemsData BattleItemsData => _battleItemsData ?? throw new InvalidOperationException("battle item data has not been initialized");

    /// <summary>Gets the current field floor map; throws when the field floor has not been initialized yet.</summary>
    private FloorMap Floor => _floor ?? throw new InvalidOperationException("field floor has not been initialized");

    /// <summary>Gets the dialogue renderer; throws when the dialogue renderer has not been initialized yet.</summary>
    private DialogueRenderer DialogueRenderer => _dialogueRenderer ?? throw new InvalidOperationException("dialogue renderer has not been initialized");

    /// <summary>Gets the field data-pack loader; throws when field data has not been loaded yet.</summary>
    private FieldDataPackLoader FieldLoader => _fieldLoader ?? throw new InvalidOperationException("field data has not been initialized");

    /// <summary>Gets the climb and action animation data; throws when action data has not been initialized yet.</summary>
    private ClimbData ActionData => _actionData ?? throw new InvalidOperationException("action data has not been initialized");

    /// <summary>Gets the field zone cache; throws when the cache is unavailable before host ready.</summary>
    private FieldZoneCache ZoneCache => _zoneCache ?? throw new InvalidOperationException("zone loader or npc data unavailable before host ready");

    /// <summary>Gets the dialogue panel texture; throws when the dialogue panel has not been initialized yet.</summary>
    private Texture2D PanelTexture => _panelTexture ?? throw new InvalidOperationException("dialogue panel texture has not been initialized");

    /// <inheritdoc/>
    BattleFlowController? IBattleTickHost.Flow => _battleFlow;

    /// <inheritdoc/>
    bool IBattleTickHost.HasFightReport => _fightReportController is not null;

    /// <inheritdoc/>
    bool IBattleTickHost.IsPlayerCameraActive => _battlePlayerCameraActive;

    /// <inheritdoc/>
    bool IBattleTickHost.IsBattleResolved => _battleResolved;

    /// <inheritdoc/>
    BattleFlowController IBattleMenuDriverHost.Flow => BattleFlow;

    /// <inheritdoc/>
    BattleMenuController IBattleMenuDriverHost.Menu => _battleMenu;

    /// <inheritdoc/>
    InputState IBattleMenuDriverHost.Input => _input.State;

    /// <inheritdoc/>
    IReadOnlyList<int> IBattleMenuDriverHost.SubmenuArguments => _battleSubmenuArgs;

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

    /// <summary>
    /// Presents the first-launch extraction overlay when the data pack is absent or invalid.
    /// Resolves the extractor binary path, wires the completion callback, and adds the screen
    /// as a child so <see cref="GameHost._Ready"/> returns immediately.
    /// </summary>
    /// <param name="dataRoot">Directory that will receive the extracted data pack.</param>
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

    /// <summary>
    /// Marshals the first-launch completion back to the main thread and continues
    /// normal session startup with the freshly extracted data pack.
    /// </summary>
    /// <param name="dataRoot">Directory containing the newly extracted data pack.</param>
    private void OnFirstLaunchComplete(string dataRoot) => _ = _firstLaunchCompletionCoordinator.CompleteAsync(dataRoot);

    /// <summary>
    /// Loads runtime assets, wires all collaborators, applies the start zone, and optionally
    /// dispatches a diagnostic probe. Exits with code 2 when the data pack is corrupt or
    /// an unexpected exception prevents initialization.
    /// </summary>
    /// <param name="dataRoot">Directory containing a validated data pack.</param>
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

    /// <summary>Finds the probe registered under an entry name in the field, dialogue, battle and storage families.</summary>
    /// <param name="entryName">Entry name chosen by a diagnostic runner, such as <c>RunWarpProbe</c>.</param>
    /// <returns>The probe body, to be started after the current frame.</returns>
    /// <exception cref="InvalidOperationException">No family registers the name.</exception>
    private Func<Task> ResolveProbe(string entryName) => _diagnosticProbeResolver.Resolve(entryName);

    #endregion

    #region Field simulation and battle coordination

    /// <summary>
    /// Executes one fixed field tick in its required order: modal dialogue gate, traversal or
    /// movement, warp and action processing, diagnostic edges, animation presentation timing, then
    /// encounter selection. Presentation receives the completed state only after this method returns.
    /// </summary>
    /// <param name="onMovementRejection">Callback invoked when the simulation rejects the requested movement.</param>
    private void SimulateStep(Action<MovementResult> onMovementRejection) =>
        new FieldTickCoordinator(this).Tick(onMovementRejection);

    /// <summary>FIELDSTG_countBattleSteps: one battle step every 8 movement frames while the
    /// player walks inside a battle area. The controller owns the countdown and the draws.</summary>
    private void CountEncounterStep()
    {
        EncounterRecord? record = _encounterStepper.Tick(_areas, _state.Position, _encounter);
        if (record is not null)
        {
            StartBattle(record);
        }
    }

    /// <summary>
    /// Transitions to a walk-triggered battle for the resolved encounter record.
    /// Logs the encounter and delegates to <see cref="EnterBattle"/>; transitions to the field
    /// on any battle setup failure.
    /// </summary>
    /// <param name="record">Encounter record resolved by the encounter stepper for the player's position.</param>
    private void StartBattle(EncounterRecord record)
    {
        try
        {
            EnterBattle(
                BuildEncounterBattle(record.Battle.Encounter),
                record.Battle.Encounter,
                $"walk encounter={record.Battle.Encounter} kind={record.Kind}");
        }
        catch (Exception error)
        {
            BattleLoadFailed(error);
        }
    }

    /// <summary>
    /// Starts the F8 debug battle using the fixed test encounter <see cref="DebugBattleEncounter"/>.
    /// On setup failure, hides the renderer and shows the on-screen diagnostic notice.
    /// </summary>
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
    /// <param name="encounterIndex">Zero-based encounter index in the encounter manifest.</param>
    /// <returns>A freshly constructed battle flow controller ready to accept the first command.</returns>
    private BattleFlowController BuildEncounterBattle(int encounterIndex) => _battleSetupCoordinator.Build(encounterIndex);

    /// <summary>
    /// Initialises the 1v1 battle session: resets all per-battle tracking, builds
    /// the renderer and HUD on first entry, applies the fight stage, restarts the
    /// entrance fade, and opens the root command menu.
    /// </summary>
    /// <param name="flow">Freshly constructed battle flow for this encounter.</param>
    /// <param name="encounter">Zero-based encounter index used for stage and log output.</param>
    /// <param name="why">Short description of why the battle started, written to the log.</param>
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
            _battleRenderer.BuildModels(
                ResolveBattleModelPath(playerId),
                ResolveBattleModelPath(enemyId),
                $"fighter-{playerId}",
                $"fighter-{enemyId}",
                enemyId);
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
        {
            _battleRenderer.BuildStage(ResolveBattleStagePath(ResolveBattleStage(encounter)), ResolveBattleStage(encounter));
        }

        RefreshBattlePresentationModels();
        _battleRenderer.RestartEntranceFade();
        _battleRenderer.SetOpeningCamera();
        _battleRenderer.Show();
        _battleMenuCoordinator.RefreshActivePartnerCatalogs();
        _battleMenu.OpenRoot(flow.RootMenuLines());
        QuietLog.Info($"battle-start {why} encounter={encounter} enemies=[{string.Join(",", _lastBattleEnemyIds)}] party=[{string.Join(",", flow.State.Players.Where(p => p is not null).Select(p => p!.Id))}] level={TestTeamGrowth.Level}");
    }

    /// <summary>
    /// Clears the active battle state, hides the renderer, and shows the on-screen diagnostic
    /// notice with the failure reason. The field remains visible so the player can continue.
    /// </summary>
    /// <param name="error">Exception thrown during battle setup or model loading.</param>
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

    /// <summary>
    /// Tears down the active battle, hides both renderers, and returns to the field.
    /// On defeat (<see cref="BattleOutcome.Lost"/>), activates the title screen overlay instead.
    /// </summary>
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
    private void TickBattle() => new BattleTickCoordinator(this).Tick();

    /// <summary>Replaces the visible models only when combat changes an active fighter.
    /// fighter metadata resolves the requested BattleFighter.Id directly via FighterEntry.id and
    /// fighter replacement flow swaps the new model into slot 0. ClearModels queues all old roots, so
    /// changing presentation cannot leak old player or enemy nodes.</summary>
    private void RefreshBattlePresentationModels() => _battlePresentationCoordinator.RefreshModels();

    /// <summary>On a victory, resolves the encounter's real reward, shares its exp by the partners
    /// who fought, applies the growth (exp + level-up via GrowthCalculator.AddExp) and builds the
    /// report view the HUD draws. Flight and defeat resolve no reward.</summary>
    /// <param name="flow">Completed battle flow whose outcome and party state are read.</param>
    private void ResolveBattleOutcome(BattleFlowController flow)
    {
        if (flow.Outcome != BattleOutcome.Won)
        {
            return;
        }

        BattleOutcomeResolution resolution = BattleOutcomeResolver.ResolveVictory(
            new BattleOutcomeRequest
            {
                Flow = flow,
                Reward = ResolveReward(_lastBattleEncounter),
                Encounter = _lastBattleEncounter,
                PartyGrowth = _partyGrowth,
                GrowthTables = _growthTables,
                Random = _battleRng,
                DigimonFor = DigimonFor,
                NameFor = NameLine,
                Text = BattleText(),
            });
        _battleReport = resolution.Report;
        _fightReportInput = resolution.FightReport;
        QuietLog.Info(resolution.Log);
    }

    /// <summary>
    /// Constructs the fight-report controller from the resolved victory input.
    /// Throws when the battle did not end in a win or the input was not resolved.
    /// </summary>
    /// <param name="flow">Completed battle flow confirming the victory outcome.</param>
    /// <returns>A fight-report controller pre-loaded with the victory data.</returns>
    /// <exception cref="InvalidOperationException">The battle outcome is not a win or the fight-report input is null.</exception>
    private FightReportController BuildFightReportController(BattleFlowController flow)
    {
        if (flow.Outcome != BattleOutcome.Won || _fightReportInput is null)
        {
            throw new InvalidOperationException("fight report requested without a resolved victory input");
        }

        _log.Warn("report-partner-slots-unavailable", "report-partner-slots-unavailable");
        return new FightReportController(_fightReportInput);
    }

    /// <summary>Builds the Digimon name map for fight report rendering from the resolved victory input.</summary>
    /// <returns>Map from fighter id to the decoded name line used in the fight report.</returns>
    private Dictionary<int, BattleTextLine> BuildReportNames() =>
        FightReportNameCatalog.Build(_fightReportInput, NameLine);

    /// <summary>The encounter's reward, or an explicit zero reward when the id is absent or
    /// out of the manifest (never invented).</summary>
    /// <param name="encounter">Zero-based encounter index to look up in the rewards manifest.</param>
    /// <returns>The reward record for the encounter, or a zero reward when the index is out of range.</returns>
    private BattleReward ResolveReward(int encounter)
    {
        if (_rewardsData is null || encounter < 0 || encounter >= _rewardsData.Rewards.Count)
        {
            return new BattleReward(0, 0, 0);
        }

        var entry = _rewardsData.Rewards[encounter];
        return new BattleReward(entry.DigimonExp, entry.Exp, entry.Money);
    }

    /// <summary>
    /// Resolves the Digimon record for a test-team party slot via the decoded fighter table.
    /// </summary>
    /// <param name="slot">Zero-based party slot index (0–2).</param>
    /// <returns>The Digimon record matching the test-team member at <paramref name="slot"/>.</returns>
    /// <exception cref="InvalidOperationException">The party, slot, or fighter table is unavailable.</exception>
    private Conversion.DigimonData DigimonFor(int slot)
    {
        if (_partyDigimonIds is null || slot < 0 || slot >= _partyDigimonIds.Count || _fighterData is null)
        {
            throw new InvalidOperationException($"test team slot {slot} has no resolved digimon");
        }

        return _fighterData.Digimon[_partyDigimonIds[slot]];
    }

    /// <summary>Advances the battle menu state machine by one input tick.</summary>
    private void TickBattleMenu() => new BattleMenuDriver(this).Tick();

    /// <summary>Constructs a text catalog over the loaded battle text data.</summary>
    /// <returns>A catalog wrapping the session's decoded battle text table.</returns>
    /// <exception cref="InvalidOperationException">Battle text data has not been loaded yet.</exception>
    private BattleTextCatalog BattleText() => new(_battleTextData ?? throw new InvalidOperationException("battle text unavailable"));

    /// <summary>DIGNAM identity is always the decoded record's nameId. The battle map covers
    /// active fighters, while the Digimon table also covers menu-only digivolution forms.</summary>
    /// <param name="fighterId">Fighter or Digimon id to look up.</param>
    /// <returns>The decoded name line for the fighter, or a missing-name sentinel when the id is unknown.</returns>
    private BattleTextLine NameLine(int fighterId)
    {
        if (_battleNameIds.TryGetValue(fighterId, out var nameId))
        {
            return BattleText().DigimonByNameId(nameId);
        }

        if (_fighterData?.Digimon.TryGetValue(fighterId, out var digimon) == true)
        {
            return BattleText().DigimonByNameId(digimon.NameId);
        }

        var enemy = _fighterData?.BattleTable.FirstOrDefault(entry => entry.Id == fighterId);
        return enemy is null ? BattleTextLine.Missing : BattleText().DigimonByNameId(enemy.NameId);
    }

    /// <summary>Builds the current battle view snapshot for presentation, or null when no battle is active.</summary>
    /// <returns>A snapshot of the current battle state for the renderer, or <see langword="null"/> when no battle is running.</returns>
    private BattleView? BuildBattleView() => _battleFlow is null
        ? null
        : BattleViewBuilder.Build(_battleFlow, _battleMenu.View, _battleReport, NameLine);

    /// <summary>The decoded battle-model JSON for a fighter id from the validated data pack.
    /// <c>DW3_BATTLE_MODEL</c> overrides the player path for isolated probes.</summary>
    /// <param name="fighterId">Fighter id whose model path to resolve.</param>
    /// <returns>Absolute path to the decoded model JSON file in the data pack.</returns>
    private string ResolveBattleModelPath(int fighterId) =>
        _options.BattleModel
        ?? BattleLoader.ModelPath(fighterId);

    /// <summary>The decoded fight-stage JSON for a stage id from the validated data pack.
    /// <c>DW3_BATTLE_STAGE</c> overrides the path for isolated probes.</summary>
    /// <param name="stageId">Stage id whose background path to resolve.</param>
    /// <returns>Absolute path to the decoded stage JSON file in the data pack.</returns>
    private string ResolveBattleStagePath(int stageId) =>
        _options.BattleStage
        ?? BattleLoader.StagePath(stageId);

    /// <summary>The fight stage (AreaBattle.stage) of an encounter, resolved from the encounter
    /// manifest: the first AreaBattle whose encounter matches. A field encounter reuses the field
    /// stage's own area battle, so this resolves the same stage the source battle would use.</summary>
    /// <param name="encounterIndex">Zero-based encounter index to look up in the encounter manifest.</param>
    /// <returns>Stage id for the encounter, used to load the fight background.</returns>
    private int ResolveBattleStage(int encounterIndex) => BattleStageResolver.Resolve(_encounterData, encounterIndex);

    #endregion

    #region Field interaction and dialogue

    /// <summary>
    /// Encounter RNG that draws from <see cref="System.Random"/>, seeded from
    /// the <c>DW3_RNG_SEED</c> environment variable when set for reproducible test runs.
    /// </summary>
    private sealed class SystemRandomEncounterRng : IEncounterRng
    {
        private readonly System.Random _random;

        /// <summary>
        /// Initializes a new instance of the <see cref="SystemRandomEncounterRng"/> class.
        /// Reads <c>DW3_RNG_SEED</c> to produce a deterministic sequence for testing.
        /// </summary>
        public SystemRandomEncounterRng()
        {
            var seedRaw = System.Environment.GetEnvironmentVariable("DW3_RNG_SEED");
            _random = int.TryParse(seedRaw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var seed)
                ? new System.Random(seed)
                : new System.Random();
        }

        /// <summary>Returns the next encounter random value in the controller's 0..4095 domain.</summary>
        /// <returns>A uniformly distributed integer in [0, 4095].</returns>
        public int Draw() => _random.Next(0, 4096);
    }

    /// <summary>The battle's RNG, mirroring SystemRandomEncounterRng (same DW3_RNG_SEED seed).</summary>
    private sealed class SystemRandomBattleRng : IBattleRng
    {
        private readonly System.Random _random;

        /// <summary>
        /// Initializes a new instance of the <see cref="SystemRandomBattleRng"/> class.
        /// Reads <c>DW3_RNG_SEED</c> to produce a deterministic sequence for testing.
        /// </summary>
        public SystemRandomBattleRng()
        {
            var seedRaw = System.Environment.GetEnvironmentVariable("DW3_RNG_SEED");
            _random = int.TryParse(seedRaw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var seed)
                ? new System.Random(seed)
                : new System.Random();
        }

        /// <summary>Returns the next battle random value in the controller's 0..4095 domain.</summary>
        /// <returns>A uniformly distributed integer in [0, 4095].</returns>
        public int NextValue() => _random.Next(0, 4096);
    }

    #endregion

    #region Field interaction and dialogue

    // After a climb lands on its own slot foot, the actor's collision circle can still
    // overlap the wall one row above the (open) foot cell, leaving the player stuck in
    // every direction. Nudge back to the nearest free cell, mirroring the source's
    // diamond-only leading-edge probes which let the player walk straight down off it.

    /// <summary>
    /// Nudges the player back to the nearest collision-free cell when a climb landing
    /// overlaps the wall above the open foot cell. Applies the same recovery as the
    /// diamond-only leading-edge probes that allow walking straight down off the platform.
    /// </summary>
    private void DepenetrateFromWall()
    {
        if (_floor is not null)
        {
            _state.Position = FieldCollisionRecovery.Depenetrate(_floor, _state.Position);
        }
    }

    /// <summary>Returns whether the player's collision circle fits at <paramref name="pos"/> without overlapping a wall cell.</summary>
    /// <param name="pos">World-space position to test.</param>
    /// <returns><see langword="true"/> when the circle at the position does not intersect any solid cell.</returns>
    private bool CircleFreeAt(NumericsVector2 pos) => FieldCollisionRecovery.CircleFreeAt(Floor, pos);

    /// <summary>FIELDSTG_setOffTrigger's climb branch: SLOT_CLIMB_UP climbs with the raw
    /// trigger direction, SLOT_CLIMB_DOWN with dir==1 ? 5 : 3 (the trigger-slot contract).</summary>
    /// <param name="climb">Climb descriptor from the current floor tile's trigger slot.</param>
    /// <param name="triggerDirection">Raw trigger direction value (1–8) from the floor map.</param>
    private void StartClimb(Climb climb, int triggerDirection) => _fieldRuntimeCoordinator.StartClimb(climb, triggerDirection);

    /// <summary>
    /// Initiates a SLOT_DROP fall: records the drop height and direction in the drop controller
    /// and logs the trigger position.
    /// </summary>
    /// <param name="drop">Drop descriptor from the current floor tile's trigger slot.</param>
    /// <param name="triggerDirection">Raw trigger direction value (1–8) from the floor map.</param>
    private void StartDrop(Drop drop, int triggerDirection)
    {
        _drop.StartDrop(_state, triggerDirection, drop.Height);
        QuietLog.Info($"drop start slot={drop.Slot} dir={triggerDirection} pos=({_state.Position.X},{_state.Position.Y}) height={drop.Height}");
    }

    /// <summary>Starts the selected talk of the actor facing the player, when one is present.
    /// Returns true when a conversation began. Unsupported controls (5 buffer insert, 8 player
    /// name) are signalled once per command and never rendered. The player stands and faces the
    /// actor it talks to.</summary>
    /// <returns><see langword="true"/> when a conversation was started; <see langword="false"/> when no eligible actor faces the player.</returns>
    private bool TryStartDialogue()
    {
        if (_dialogueData is null || _currentZone is null)
        {
            return false;
        }

        return DialogueStarter.TryStart(
            new DialogueStartRequest
            {
                Data = _dialogueData,
                Zone = _currentZone,
                Player = _state,
                Controller = _dialogue,
                Warn = _log.Warn,
            },
            out _dialogueBoxWidth);
    }

    /// <summary>Builds the render view for the active conversation: the box origin and type are
    /// computed from the actor's source position, the camera and its facing, exactly like
    /// FIELDSTG_createTalk + FIELDSTG_getSpeechPos.</summary>
    /// <returns>A dialogue view snapshot containing the box position, type, width, and current page state.</returns>
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

    /// <summary>
    /// Injects held-key events for the specified semantic direction, releasing all four
    /// movement keys first. Used by diagnostic drivers that steer the player without UI interaction.
    /// </summary>
    /// <param name="direction">Semantic movement direction to simulate as held keyboard input.</param>
    private void SetTestHeldDirection(SemanticDirection direction)
    {
        foreach (var key in new[] { Key.Q, Key.D, Key.Z, Key.S })
        {
            _input.Handle(new InputEventKey { Keycode = key, Pressed = false });
        }

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
        {
            _input.Handle(new InputEventKey { Keycode = key, Pressed = true });
        }
    }

    /// <summary>
    /// Renders one field frame, waits for the GPU to finish drawing, and saves a PNG screenshot.
    /// Used by diagnostic probes to capture reference images.
    /// </summary>
    /// <param name="captureRoot">Directory where the capture file is written.</param>
    /// <param name="fileName">File name of the capture PNG relative to <paramref name="captureRoot"/>.</param>
    /// <returns>A task that completes when the image has been written to disk.</returns>
    private async System.Threading.Tasks.Task CaptureStill(string captureRoot, string fileName)
    {
        _renderer?.Present(_state, CurrentZone, _input.F3On);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var capturePath = Path.Combine(captureRoot, fileName);
        Require(GetViewport().GetTexture().GetImage().SavePng(capturePath) == Error.Ok, $"capture write failed={capturePath}");
    }

    /// <summary>Fires a synthetic F3 key press and release to toggle the diagnostic overlay.</summary>
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

    /// <summary>
    /// Resolves a zone by id, but throws <see cref="CacheResourceException"/> when the
    /// warp probe's missing-target flag is set, forcing the probe through its error path.
    /// </summary>
    /// <param name="zoneId">Normalized zone id (<c>Snnn</c>) to resolve.</param>
    /// <returns>The built zone.</returns>
    /// <exception cref="CacheResourceException">The warp probe missing-target flag is active.</exception>
    private FieldZone ResolveTarget(string zoneId)
    {
        if (_options.WarpProbeMissingTarget)
        {
            throw new CacheResourceException("CACHE_RESOURCE_UNAVAILABLE", $"zone={zoneId} forced unavailable for probe");
        }

        return GetOrLoadZone(zoneId);
    }

    /// <summary>
    /// Commits a loaded field scene to the active session. It rebinds collision, encounter, and
    /// occupant state before rebuilding renderer nodes, resets the player icon at the current actor
    /// position, and presents the completed scene once before the next simulation tick.
    /// </summary>
    /// <param name="zone">The built field zone to apply as the active scene.</param>
    private void ApplyScene(FieldZone zone) => _fieldRuntimeCoordinator.ApplyScene(zone);

    /// <summary>
    /// Resolves the PNG path for a player animation frame from the field data-pack.
    /// </summary>
    /// <param name="frame">Zero-based animation frame index.</param>
    /// <returns>Absolute path to the player frame PNG in the data pack.</returns>
    /// <exception cref="InvalidOperationException">The field loader is unavailable before host ready.</exception>
    private string ResolvePlayerFrame(int frame)
    {
        return _fieldLoader?.PlayerFramePath(frame)
            ?? throw new InvalidOperationException("field data-pack loader unavailable before host ready");
    }

    /// <summary>
    /// Resolves the PNG path for a climb or drop action animation frame from the field data-pack.
    /// Animation sets 0x16–0x19 are drop frames; all other sets are climb frames.
    /// </summary>
    /// <param name="set">Animation set index; 0x16–0x19 selects drop frames, others select climb frames.</param>
    /// <param name="frame">Zero-based frame index within the selected animation set.</param>
    /// <returns>Absolute path to the action frame PNG in the data pack.</returns>
    /// <exception cref="InvalidOperationException">The animation loader is unavailable before host ready.</exception>
    private string ResolveActionFrame(int set, int frame)
    {
        var loader = _animationLoader ?? throw new InvalidOperationException("field animation loader unavailable before host ready");
        return set is >= 0x16 and <= 0x19 ? loader.DropFramePath(frame) : loader.ClimbFramePath(frame);
    }

    /// <summary>Returns the directory that probe captures should be written to, creating
    /// it if it does not yet exist. Prefers the explicit DW3_CAPTURE_DIR option; otherwise
    /// places a "captures" subdirectory under <paramref name="dataRoot"/>. Shared by all
    /// probe methods to avoid repeating the inline null-coalesce + CreateDirectory pattern.</summary>
    /// <param name="dataRoot">Session data directory used as the fallback capture root.</param>
    /// <returns>Absolute path to the capture directory, guaranteed to exist.</returns>
    private string ProbeOutputRoot(string dataRoot)
    {
        var root = _options.CaptureDir ?? Path.Combine(dataRoot, "captures");
        Directory.CreateDirectory(root);
        return root;
    }

    /// <summary>Asserts <paramref name="condition"/> and throws <see cref="ProbeRequirement"/> with <paramref name="message"/> when false.</summary>
    /// <param name="condition">The condition that must be true.</param>
    /// <param name="message">Failure message passed to the probe requirement exception.</param>
    private static void Require(bool condition, string message) => ProbeRequirement.Require(condition, message);

    #endregion
}
