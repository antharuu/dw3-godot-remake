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

    /// <summary>Gets the decoded dialogue font used by battle text, or null before startup completes.</summary>
    DialogueData? Dialogue { get; }

    /// <summary>Gets a value indicating whether a battle setup is active.</summary>
    bool HasActiveBattle { get; }

    /// <summary>Gets the fighter id currently rendered on the player side.</summary>
    int RenderedPlayerId { get; }

    /// <summary>Returns the probe output directory, honoring the capture override.</summary>
    /// <param name="relativeFallback">Fallback path below the reference or data root.</param>
    /// <returns>An existing absolute directory.</returns>
    string PrepareCaptureDirectory(string relativeFallback);

    /// <summary>Applies the S200 field scene and records it as the live zone.</summary>
    void ApplyS200();

    /// <summary>Starts the same manifest-backed battle as the F8 debug shortcut.</summary>
    void StartDebugBattle();

    /// <summary>Runs one fixed battle simulation step.</summary>
    void TickTestSimulation();

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
}
