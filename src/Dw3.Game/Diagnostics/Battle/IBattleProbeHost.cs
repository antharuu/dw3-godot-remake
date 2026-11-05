using Dw3Production.Conversion;
using Dw3Production.Input;
using Dw3Production.Presentation;
using Dw3Production.Simulation;
using Godot;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>
/// Restricts scripted battle diagnostics to the live battle state, renderer, injected input and
/// frame-capture operations they need. It does not expose field simulation or startup internals.
/// </summary>
internal interface IBattleProbeHost
{
    /// <summary>Gets the live player and field state retained across the battle.</summary>
    GameState State { get; }

    /// <summary>Gets the input adapter used to inject the same key edges as interactive play.</summary>
    InputAdapter Input { get; }

    /// <summary>Gets the command-menu controller rendered by the active battle.</summary>
    BattleMenuController Menu { get; }

    /// <summary>Gets the active battle renderer.</summary>
    /// <exception cref="InvalidOperationException">No battle renderer exists.</exception>
    BattleRenderer Renderer { get; }

    /// <summary>Gets the active battle flow.</summary>
    /// <exception cref="InvalidOperationException">No battle flow exists.</exception>
    BattleFlowController Flow { get; }

    /// <summary>Gets the random source used by battle command resolution.</summary>
    IBattleRng BattleRng { get; }

    /// <summary>Gets the decoded dialogue font used by battle text, or null before startup completes.</summary>
    DialogueData? Dialogue { get; }

    /// <summary>Gets the decoded fighter manifest, or null before startup completes.</summary>
    FighterData? FighterData { get; }

    /// <summary>Gets the data-pack directory holding the decoded fighter battle models.</summary>
    string BattleModelsDirectory { get; }

    /// <summary>Gets the decoded technique manifest, or null before startup completes.</summary>
    TechniqueData? TechData { get; }

    /// <summary>Gets a value indicating whether encounter data was loaded at startup.</summary>
    bool HasEncounterData { get; }

    /// <summary>Gets a value indicating whether the report data and party growth state are available.</summary>
    bool HasReportData { get; }

    /// <summary>Gets a value indicating whether a battle renderer has been constructed.</summary>
    bool HasBattleRenderer { get; }

    /// <summary>Gets a value indicating whether a battle setup is active.</summary>
    bool HasActiveBattle { get; }

    /// <summary>Gets the fighter id currently rendered on the player side.</summary>
    int RenderedPlayerId { get; }

    /// <summary>Gets the last entered battle encounter id.</summary>
    int LastBattleEncounter { get; }

    /// <summary>Gets the enemy fighter ids saved when the active battle started.</summary>
    IReadOnlyList<int> LastBattleEnemyIds { get; }

    /// <summary>Gets the victory report resolved for the active battle, when one exists.</summary>
    BattleReportView? BattleReport { get; }

    /// <summary>Gets the report controller while the report owns input.</summary>
    FightReportController? FightReportController { get; }

    /// <summary>Gets a value indicating whether the report renderer is visible.</summary>
    bool IsFightReportVisible { get; }

    /// <summary>Gets the test party's persistent growth records, or null before startup.</summary>
    IReadOnlyList<PartnerGrowthState>? PartyGrowth { get; }

    /// <summary>Gets the fixed victory-to-report fade duration in simulation steps.</summary>
    int ReportFadeFrames { get; }

    /// <summary>Gets a value indicating whether a defeat reached the title screen.</summary>
    bool IsInTitle { get; }

    /// <summary>Gets a value indicating whether the title-screen node is visible.</summary>
    bool IsTitleVisible { get; }

    /// <summary>Gets the current battle-load notice text.</summary>
    string? BattleNoticeText { get; }

    /// <summary>Gets a value indicating whether the battle-load notice is visible.</summary>
    bool IsBattleNoticeVisible { get; }

    /// <summary>Gets the resolved display name for a fighter.</summary>
    /// <param name="fighterId">Source fighter id.</param>
    /// <returns>The decoded display text.</returns>
    BattleTextLine NameLine(int fighterId);

    /// <summary>Returns the probe output directory, honoring the capture override.</summary>
    /// <param name="relativeFallback">Fallback path below the reference or data root.</param>
    /// <returns>An existing absolute directory.</returns>
    string PrepareCaptureDirectory(string relativeFallback);

    /// <summary>Applies the S200 field scene and records it as the live zone.</summary>
    void ApplyS200();

    /// <summary>Captures the current field presentation after a frame-post-draw boundary.</summary>
    /// <param name="captureRoot">Directory receiving the PNG.</param>
    /// <param name="fileName">PNG file name.</param>
    /// <returns>A task completed when the PNG has been written.</returns>
    Task CaptureStillAsync(string captureRoot, string fileName);

    /// <summary>Starts the same manifest-backed battle as the F8 debug shortcut.</summary>
    void StartDebugBattle();

    /// <summary>Starts an unmodified source encounter through the ordinary battle construction path.</summary>
    /// <param name="encounter">Encounter index from the local data pack.</param>
    void StartEncounterForProbe(int encounter);

    /// <summary>Resolves the decoded battle model file of a fighter.</summary>
    /// <param name="fighterId">Source fighter id.</param>
    /// <returns>The model JSON path in the data pack.</returns>
    string ResolveBattleModelPath(int fighterId);

    /// <summary>Resolves the decoded battle stage file of a stage id.</summary>
    /// <param name="stageId">Source battle stage id.</param>
    /// <returns>The stage JSON path in the data pack.</returns>
    string ResolveBattleStagePath(int stageId);

    /// <summary>Resolves a registry arena without the active-stage override.</summary>
    /// <param name="stageId">Registered arena identifier.</param>
    /// <returns>The data-pack stage document.</returns>
    string ResolveRegisteredStageForProbe(int stageId);

    /// <summary>Records a fighter built outside the battle flow as the rendered player and releases the command camera.</summary>
    /// <param name="fighterId">Source fighter id rendered by the sheet.</param>
    void AdoptSheetFighter(int fighterId);

    /// <summary>Runs one fixed battle simulation step.</summary>
    void TickTestSimulation();

    /// <summary>Refreshes active fighter command, technique, and digivolution catalogs.</summary>
    void RefreshActivePartnerCatalogs();

    /// <summary>Resolves an encounter reward or an explicit zero reward when it is absent.</summary>
    /// <param name="encounter">Manifest encounter id.</param>
    /// <returns>The corresponding reward.</returns>
    BattleReward ResolveReward(int encounter);

    /// <summary>Temporarily removes fighter data while an operation exercises the load-failure path.</summary>
    /// <param name="operation">Operation that must see unavailable fighter data.</param>
    void WithFighterDataUnavailable(Action operation);

    /// <summary>Builds the current presentation snapshot.</summary>
    /// <returns>The view, or null when no battle flow exists.</returns>
    BattleView? BuildBattleView();

    /// <summary>Returns the decoded battle text catalog.</summary>
    /// <returns>The catalog backed by the loaded data pack.</returns>
    BattleTextCatalog BattleText();

    /// <summary>Opens a production submenu for a probe-selected set of entries.</summary>
    /// <param name="kind">Submenu layout to open.</param>
    /// <param name="entries">Argument and decoded text pairs.</param>
    /// <param name="enabled">Optional availability predicate.</param>
    /// <param name="line">Optional line projection.</param>
    void OpenSubmenu(
        BattleMenuKind kind,
        IReadOnlyList<(int Arg, BattleTextLine Text)> entries,
        Func<int, bool>? enabled = null,
        Func<int, BattleTextLine, BattleMenuLine>? line = null);

    /// <summary>Returns the inactive party slots shown by the switch menu.</summary>
    /// <param name="flow">Active battle flow.</param>
    /// <returns>Slot and decoded name pairs.</returns>
    IReadOnlyList<(int Slot, BattleTextLine Name)> SwitchLines(BattleFlowController flow);

    /// <summary>Waits until the rendering server has completed the next frame.</summary>
    /// <returns>The frame-post-draw signal awaiter.</returns>
    SignalAwaiter NextFrameDrawn();

    /// <summary>Copies the last rendered viewport image.</summary>
    /// <returns>The 1280x720 viewport image.</returns>
    Image CaptureViewport();

    /// <summary>Loads a source comparison image from the configured private reference directory.</summary>
    /// <param name="subPath">Relative reference PNG path.</param>
    /// <returns>The decoded image.</returns>
    /// <exception cref="PsxReferenceUnavailableException">The reference root or image is unavailable.</exception>
    Image LoadPsxReference(string subPath);

    /// <summary>Stops the engine with the probe verdict.</summary>
    /// <param name="exitCode">0 for pass, 3 for failure, 5 for an unavailable private oracle.</param>
    void Quit(int exitCode);

    /// <summary>
    /// Schedules a stop of the engine with the probe verdict, deferred to the next main-loop
    /// frame.  Use this instead of <see cref="Quit"/> when calling from an async continuation
    /// so that the main loop can settle before the engine tears down its native objects.
    /// </summary>
    /// <param name="exitCode">0 for pass, 3 for failure, 5 for an unavailable private oracle.</param>
    void QuitDeferred(int exitCode);
}
