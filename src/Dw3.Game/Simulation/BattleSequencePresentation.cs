using Dw3Production.Conversion;
using Dw3Production.Presentation;

namespace Dw3Production.Simulation;

/// <summary>Preserves the displayed roster until the action script and its reactions finish.
/// Rules are pre-resolved once, while result HP is published after playback, not at a reaction request.</summary>
public sealed class BattleSequencePresentation
{
    private IReadOnlyList<BattleFighterView> _players = [];
    private IReadOnlyList<BattleFighterView> _enemies = [];

    /// <summary>Gets the frozen player slots, including their original active marker.</summary>
    public IReadOnlyList<BattleFighterView> Players => _players;

    /// <summary>Gets the frozen enemy slots, including the target before an automatic relay.</summary>
    public IReadOnlyList<BattleFighterView> Enemies => _enemies;

    /// <summary>Captures presentation values before a command can mutate rules or replace an actor.</summary>
    /// <param name="state">State before resolution.</param>
    public void Capture(BattleState state)
    {
        _players = BattleViewBuilder.BuildFighters(state.Players, state.ActivePlayer, _ => BattleTextLine.Missing);
        _enemies = BattleViewBuilder.BuildFighters(state.Enemies, state.ActiveEnemy, _ => BattleTextLine.Missing);
    }
}
