namespace Dw3Production.Conversion;

/// <summary>Prepared, validated non-music cue with immutable note timing.</summary>
/// <param name="Commands">Resolved note transitions in stable source order.</param>
/// <param name="EndSeconds">Authored end-of-track deadline.</param>
/// <param name="LoopSeconds">Authored music loop marker, when the track declares one.</param>
public sealed record PreparedAudioSequence(IReadOnlyList<AudioSequenceCommand> Commands, double EndSeconds, double? LoopSeconds = null);
