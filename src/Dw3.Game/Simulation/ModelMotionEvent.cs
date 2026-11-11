namespace Dw3Production.Simulation;

/// <summary>A fighter model motion or visibility change requested by the script.</summary>
/// <param name="AttackerSide">Side that owns the script.</param>
/// <param name="Subcommand">0 = set motion, 1 = show, 2 = hide.</param>
/// <param name="Actor">Raw actor index from the script.</param>
/// <param name="Motion">Motion index; meaningful only when <paramref name="Subcommand"/> is 0.</param>
public sealed record ModelMotionEvent(BattleSide AttackerSide, int Subcommand, int Actor, int Motion) : BattleSequenceEvent;
