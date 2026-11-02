using Dw3Production.Conversion;
using Dw3Production.Diagnostics.Battle;
using Dw3Production.Input;
using Dw3Production.Presentation;
using Dw3Production.Simulation;
using Godot;

namespace Dw3Production;

/// <summary>Forwards the narrow battle-probe contract to the running game session.</summary>
public partial class GameHost : IBattleProbeHost
{
    /// <inheritdoc/>
    GameState IBattleProbeHost.State => _state;

    /// <inheritdoc/>
    InputAdapter IBattleProbeHost.Input => _input;

    /// <inheritdoc/>
    BattleMenuController IBattleProbeHost.Menu => _battleMenu;

    /// <inheritdoc/>
    BattleRenderer IBattleProbeHost.Renderer => BattleRenderer;

    /// <inheritdoc/>
    BattleFlowController IBattleProbeHost.Flow => BattleFlow;

    /// <inheritdoc/>
    IBattleRng IBattleProbeHost.BattleRng => _battleRng;

    /// <inheritdoc/>
    DialogueData? IBattleProbeHost.Dialogue => _dialogueData;

    /// <inheritdoc/>
    bool IBattleProbeHost.HasEncounterData => _encounterData is not null;

    /// <inheritdoc/>
    bool IBattleProbeHost.HasReportData => _rewardsData is not null && _growthTables is not null && _partyGrowth is not null;

    /// <inheritdoc/>
    bool IBattleProbeHost.HasActiveBattle => _activeBattle is not null;

    /// <inheritdoc/>
    int IBattleProbeHost.RenderedPlayerId => _battleRenderedPlayerId;

    /// <inheritdoc/>
    int IBattleProbeHost.LastBattleEncounter => _lastBattleEncounter;

    /// <inheritdoc/>
    IReadOnlyList<int> IBattleProbeHost.LastBattleEnemyIds => _lastBattleEnemyIds;

    /// <inheritdoc/>
    BattleReportView? IBattleProbeHost.BattleReport => _battleReport;

    /// <inheritdoc/>
    FightReportController? IBattleProbeHost.FightReportController => _fightReportController;

    /// <inheritdoc/>
    bool IBattleProbeHost.IsFightReportVisible => _fightReportRenderer is { Visible: true };

    /// <inheritdoc/>
    IReadOnlyList<PartnerGrowthState>? IBattleProbeHost.PartyGrowth => _partyGrowth;

    /// <inheritdoc/>
    int IBattleProbeHost.ReportFadeFrames => BattleReportFadeFrames;

    /// <inheritdoc/>
    bool IBattleProbeHost.IsInTitle => _inTitle;

    /// <inheritdoc/>
    bool IBattleProbeHost.IsTitleVisible => _titleScreen is { Visible: true };

    /// <inheritdoc/>
    string? IBattleProbeHost.BattleNoticeText => _battleNoticeText;

    /// <inheritdoc/>
    bool IBattleProbeHost.IsBattleNoticeVisible => _battleNoticeLabel is { Visible: true };

    /// <inheritdoc/>
    string IBattleProbeHost.PrepareCaptureDirectory(string relativeFallback)
    {
        var baseRoot = _options.ReferenceDir ?? _dataRoot;
        var captureRoot = _options.CaptureDir ?? Path.Combine(baseRoot, relativeFallback);
        Directory.CreateDirectory(captureRoot);
        return captureRoot;
    }

    /// <inheritdoc/>
    void IBattleProbeHost.ApplyS200()
    {
        ApplyScene(GetOrLoadZone("S200"));
        _state.ZoneId = "S200";
    }

    /// <inheritdoc/>
    Task IBattleProbeHost.CaptureStillAsync(string captureRoot, string fileName) => CaptureStill(captureRoot, fileName);

    /// <inheritdoc/>
    void IBattleProbeHost.StartDebugBattle() => StartDebugBattle();

    /// <inheritdoc/>
    void IBattleProbeHost.TickTestSimulation() => TickTestSimulation();

    /// <inheritdoc/>
    void IBattleProbeHost.RefreshActivePartnerCatalogs() => RefreshActivePartnerCatalogs();

    /// <inheritdoc/>
    BattleReward IBattleProbeHost.ResolveReward(int encounter) => ResolveReward(encounter);

    /// <inheritdoc/>
    void IBattleProbeHost.WithFighterDataUnavailable(Action operation)
    {
        var savedFighterData = _fighterData;
        _fighterData = null;
        try
        {
            operation();
        }
        finally
        {
            _fighterData = savedFighterData;
        }
    }

    /// <inheritdoc/>
    BattleView? IBattleProbeHost.BuildBattleView() => BuildBattleView();

    /// <inheritdoc/>
    BattleTextCatalog IBattleProbeHost.BattleText() => BattleText();

    /// <inheritdoc/>
    void IBattleProbeHost.OpenSubmenu(
        BattleMenuKind kind,
        IReadOnlyList<(int Arg, BattleTextLine Text)> entries,
        Func<int, bool>? enabled,
        Func<int, BattleTextLine, BattleMenuLine>? line) => OpenSubmenu(kind, entries, enabled, line);

    /// <inheritdoc/>
    IReadOnlyList<(int Slot, BattleTextLine Name)> IBattleProbeHost.SwitchLines(BattleFlowController flow) =>
        SwitchLines(flow);

    /// <inheritdoc/>
    SignalAwaiter IBattleProbeHost.NextFrameDrawn() =>
        ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

    /// <inheritdoc/>
    Image IBattleProbeHost.CaptureViewport() => GetViewport().GetTexture().GetImage();

    /// <inheritdoc/>
    Image IBattleProbeHost.LoadPsxReference(string subPath) => LoadPsxReference(subPath);

    /// <inheritdoc/>
    void IBattleProbeHost.Quit(int exitCode) => GetTree().Quit(exitCode);
}
