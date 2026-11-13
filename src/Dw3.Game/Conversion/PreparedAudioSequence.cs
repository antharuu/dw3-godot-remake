namespace Dw3Production.Conversion;

/// <summary>Prepared, validated non-music cue with immutable note timing.</summary>
/// <param name="Commands">Resolved note transitions in stable source order.</param>
/// <param name="EndSeconds">Authored end-of-track deadline.</param>
/// <param name="LoopSeconds">Authored loop-start marker (CC99=20), when the track declares one.</param>
/// <param name="LoopEndSeconds">Authored loop-end marker (CC99=30) where playback jumps back, when declared.</param>
public sealed record PreparedAudioSequence(IReadOnlyList<AudioSequenceCommand> Commands, double EndSeconds, double? LoopSeconds = null, double? LoopEndSeconds = null);
