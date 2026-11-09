namespace Dw3.Formats.Battle;

/// <summary>PCM data and loop metadata recovered from one terminated SPU ADPCM sample stream.</summary>
/// <param name="PcmSamples">Decoded signed 16-bit mono PCM samples before terminal padding.</param>
/// <param name="EncodedBytesConsumed">Bytes through the terminal ADPCM block, excluding optional padding.</param>
/// <param name="Loops">Whether the terminal block requests repetition from the loop start.</param>
/// <param name="LoopStartSample">PCM sample index of the last loop-start block, if one exists.</param>
public sealed record DecodedSpuAdpcmSample(short[] PcmSamples, int EncodedBytesConsumed, bool Loops, int? LoopStartSample);
