namespace Dw3Production.Simulation;

/// <summary>Requests an additional fighter lifecycle or an authored root rotation, never an effect-model alias.</summary>
/// <param name="AttackerSide">Side owning the script-local actor slots.</param>
/// <param name="Arguments">Unmodified model subcommand operands.</param>
public sealed record ModelControlEvent(BattleSide AttackerSide, IReadOnlyList<short> Arguments) : BattleSequenceEvent;
