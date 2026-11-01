using Dw3Production.Conversion;
using Dw3Production.Simulation;

namespace Dw3Production.Presentation;

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
