using Dw3Production.Conversion;

namespace Dw3Production;

/// <summary>Contains the battle action-sequence coordination bridge for <see cref="GameHost"/>.
/// All lifecycle and dispatch logic lives in <see cref="BattleSequenceRunner"/>; this partial
/// class only holds the field and the entry points, keeping GameHost's DW3001 budget within limit.</summary>
public partial class GameHost
{
    private BattleSequenceRunner? _sequenceRunner;

    /// <summary>Advances the active action-sequence timeline by one frame.
    /// Delegates entirely to the runner so GameHost adds minimal complexity.</summary>
    private void TickBattleActionSequence() => _sequenceRunner?.Tick();

    /// <summary>Builds and wires the sequence runner once the battle flow and pack are both ready.</summary>
    /// <param name="provider">Script provider backed by the battle data pack.</param>
    private void InitBattleSequenceRunner(IActionScriptProvider provider)
    {
        _sequenceRunner = new BattleSequenceRunner(BattleFlow, provider);
        _sequenceRunner.SetRenderer(_battleRenderer);
    }

    /// <summary>Tears down the runner when a battle ends.</summary>
    private void TearDownBattleSequenceRunner()
    {
        _sequenceRunner?.Cancel();
        _sequenceRunner = null;
    }
}
