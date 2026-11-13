using Dw3Production.Conversion;
using Dw3Production.Simulation;

namespace Dw3Production.Presentation;

/// <summary>The render view for one frame of the battle, computed by the host from the flow
/// controller. The models are posed and shown/hidden from this, and the HUD reads the rest.</summary>
/// <param name="Phase">Turn phase.</param>
/// <param name="Outcome">Battle outcome.</param>
/// <param name="Players">Player slots.</param>
/// <param name="Enemies">Enemy slots.</param>
/// <param name="Message">Debug label of the message.</param>
/// <param name="Menu">Command menu, while the player commands.</param>
/// <param name="Report">Victory report, when resolved.</param>
/// <param name="MessageText">Message text.</param>
/// <param name="MessageVisible">Whether the message box is shown.</param>
/// <param name="MessageLines">Message lines revealed so far.</param>
/// <param name="MessageArrow">Whether the continuation arrow is shown.</param>
public readonly record struct BattleView(
    BattleFlowPhase Phase,
    BattleOutcome Outcome,
    IReadOnlyList<BattleFighterView> Players,
    IReadOnlyList<BattleFighterView> Enemies,
    string Message,
    BattleMenuView? Menu,
    BattleReportView? Report = null,
    BattleTextLine? MessageText = null,
    bool MessageVisible = false,
    int MessageLines = 2,
    bool MessageArrow = true);
