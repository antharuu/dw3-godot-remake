namespace Dw3Production.Simulation;

/// <summary>A sound playback request from the script.</summary>
/// <param name="AttackerSide">Side that owns the script.</param>
/// <param name="SoundId">Sound identifier to play.</param>
/// <param name="Timing">Timing parameter from the script.</param>
public sealed record SoundRequestEvent(BattleSide AttackerSide, int SoundId, int Timing) : BattleSequenceEvent;
