namespace Dw3.Formats.Battle;

/// <summary>Metadata carried by one PlayStation VAB header embedded in a battle sound archive.</summary>
/// <param name="Version">VAB format version.</param>
/// <param name="Id">Source bank identifier.</param>
/// <param name="FileSize">Declared complete VAB size, including its body.</param>
/// <param name="ProgramCount">Number of instrument programs.</param>
/// <param name="ToneCount">Number of tones referenced by the programs.</param>
/// <param name="SampleCount">Number of ADPCM sample bodies.</param>
/// <param name="MasterVolume">Bank master volume.</param>
/// <param name="MasterPan">Bank master stereo pan.</param>
public sealed record SoundBankHeader(
    uint Version,
    uint Id,
    uint FileSize,
    ushort ProgramCount,
    ushort ToneCount,
    ushort SampleCount,
    byte MasterVolume,
    byte MasterPan);
