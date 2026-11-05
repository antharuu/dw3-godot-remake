using Dw3Production.Conversion;
using Dw3Production.Diagnostics.Battle;
using Dw3Production.Input;
using Dw3Production.Presentation;
using Dw3Production.Simulation;
using Godot;

namespace Dw3Production;

/// <summary>Contains GameHost's battle probe context implementation, exposing the narrow <see cref="IBattleProbeHost"/> contract.</summary>
public partial class GameHost
{
    /// <summary>
    /// Battle diagnostic context handed to battle probes. It exposes only the narrow
    /// <see cref="IBattleProbeHost"/> contract over the running session, so the probes never see
    /// the wider GameHost duties.
    /// </summary>
    private sealed class BattleProbeContext : IBattleProbeHost
    {
        /// <summary>Running game session the context reads and drives.</summary>
        private readonly GameHost _host;

        /// <summary>Initializes a new instance of the <see cref="BattleProbeContext"/> class.</summary>
        /// <param name="host">Running game session.</param>
        public BattleProbeContext(GameHost host)
        {
            _host = host;
        }

        /// <inheritdoc/>
        public GameState State => _host._state;

        /// <inheritdoc/>
        public InputAdapter Input => _host._input;

        /// <inheritdoc/>
        public BattleMenuController Menu => _host._battleMenu;

        /// <inheritdoc/>
        public BattleRenderer Renderer => _host.BattleRenderer;

        /// <inheritdoc/>
        public BattleFlowController Flow => _host.BattleFlow;

        /// <inheritdoc/>
        public IBattleRng BattleRng => _host._battleRng;

        /// <inheritdoc/>
        public DialogueData? Dialogue => _host._dialogueData;

        /// <inheritdoc/>
        public FighterData? FighterData => _host._fighterData;

        /// <inheritdoc/>
        public string BattleModelsDirectory => Path.Combine(_host._dataRoot, "data", "battle", "models");

        /// <inheritdoc/>
        public TechniqueData? TechData => _host._techData;

        /// <inheritdoc/>
        public bool HasEncounterData => _host._encounterData is not null;

        /// <inheritdoc/>
        public bool HasReportData =>
            _host._rewardsData is not null && _host._growthTables is not null && _host._partyGrowth is not null;

        /// <inheritdoc/>
        public bool HasBattleRenderer => _host._battleRenderer is not null;

        /// <inheritdoc/>
        public bool HasActiveBattle => _host._activeBattle is not null;

        /// <inheritdoc/>
        public int RenderedPlayerId => _host._battleRenderedPlayerId;

        /// <inheritdoc/>
        public int LastBattleEncounter => _host._lastBattleEncounter;

        /// <inheritdoc/>
        public IReadOnlyList<int> LastBattleEnemyIds => _host._lastBattleEnemyIds;

        /// <inheritdoc/>
        public BattleReportView? BattleReport => _host._battleReport;

        /// <inheritdoc/>
        public FightReportController? FightReportController => _host._fightReportController;

        /// <inheritdoc/>
        public bool IsFightReportVisible => _host._fightReportRenderer is { Visible: true };

        /// <inheritdoc/>
        public IReadOnlyList<PartnerGrowthState>? PartyGrowth => _host._partyGrowth;

        /// <inheritdoc/>
        public int ReportFadeFrames => BattleReportFadeFrames;

        /// <inheritdoc/>
        public bool IsInTitle => _host._inTitle;

        /// <inheritdoc/>
        public bool IsTitleVisible => _host._titleScreen is { Visible: true };

        /// <inheritdoc/>
        public string? BattleNoticeText => _host._battleNoticeText;

        /// <inheritdoc/>
        public bool IsBattleNoticeVisible => _host._battleNoticeLabel is { Visible: true };

        /// <inheritdoc/>
        public BattleTextLine NameLine(int fighterId) => _host.NameLine(fighterId);

        /// <inheritdoc/>
        public string PrepareCaptureDirectory(string relativeFallback)
        {
            var baseRoot = _host._options.ReferenceDir ?? _host._dataRoot;
            var captureRoot = _host._options.CaptureDir ?? Path.Combine(baseRoot, relativeFallback);
            Directory.CreateDirectory(captureRoot);
            return captureRoot;
        }

        /// <inheritdoc/>
        public void ApplyS200()
        {
            _host.ApplyScene(_host.GetOrLoadZone("S200"));
            _host._state.ZoneId = "S200";
        }

        /// <inheritdoc/>
        public Task CaptureStillAsync(string captureRoot, string fileName) => _host.CaptureStill(captureRoot, fileName);

        /// <inheritdoc/>
        public void StartDebugBattle() => _host.StartDebugBattle();

        /// <inheritdoc/>
        public void StartEncounterForProbe(int encounter) =>
            _host.EnterBattle(_host.BuildEncounterBattle(encounter), encounter, "native action probe source encounter");

        /// <inheritdoc/>
        public string ResolveBattleModelPath(int fighterId) => _host.ResolveBattleModelPath(fighterId);

        /// <inheritdoc/>
        public string ResolveBattleStagePath(int stageId) => _host.ResolveBattleStagePath(stageId);

        /// <inheritdoc/>
        public string ResolveRegisteredStageForProbe(int stageId) => _host.BattleLoader.StagePath(stageId);

        /// <inheritdoc/>
        public void AdoptSheetFighter(int fighterId)
        {
            _host._battlePlayerCameraActive = false;
            _host._battleRenderedPlayerId = fighterId;
        }

        /// <inheritdoc/>
        public void TickTestSimulation() => _host.TickTestSimulation();

        /// <inheritdoc/>
        public void RefreshActivePartnerCatalogs() => _host._battleMenuCoordinator.RefreshActivePartnerCatalogs();

        /// <inheritdoc/>
        public BattleReward ResolveReward(int encounter) => _host.ResolveReward(encounter);

        /// <inheritdoc/>
        public void WithFighterDataUnavailable(Action operation)
        {
            var savedFighterData = _host._fighterData;
            _host._fighterData = null;
            try
            {
                operation();
            }
            finally
            {
                _host._fighterData = savedFighterData;
            }
        }

        /// <inheritdoc/>
        public BattleView? BuildBattleView() => _host.BuildBattleView();

        /// <inheritdoc/>
        public BattleTextCatalog BattleText() => _host.BattleText();

        /// <inheritdoc/>
        public void OpenSubmenu(
            BattleMenuKind kind,
            IReadOnlyList<(int Arg, BattleTextLine Text)> entries,
            Func<int, bool>? enabled = null,
            Func<int, BattleTextLine, BattleMenuLine>? line = null) => _host._battleMenuCoordinator.OpenSubmenu(kind, entries, enabled, line);

        /// <inheritdoc/>
        public IReadOnlyList<(int Slot, BattleTextLine Name)> SwitchLines(BattleFlowController flow) =>
            _host._battleMenuCoordinator.SwitchLines(flow);

        /// <inheritdoc/>
        public SignalAwaiter NextFrameDrawn() =>
            _host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

        /// <inheritdoc/>
        public Image CaptureViewport() => _host.GetViewport().GetTexture().GetImage();

        /// <inheritdoc/>
        public Image LoadPsxReference(string subPath) => PsxReferenceImages.Load(subPath);

        /// <inheritdoc/>
        public void Quit(int exitCode) => _host.GetTree().Quit(exitCode);

        /// <inheritdoc/>
        public void QuitDeferred(int exitCode) =>
            Callable.From(() => _host.GetTree().Quit(exitCode)).CallDeferred();
    }
}
