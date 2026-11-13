using Dw3.Formats.Battle;

namespace Dw3.Extractor;

/// <summary>Projects every SEP group and event into the private prepared audio pack.</summary>
internal static class BattleSequencePreparation
{
    /// <summary>Prepares source timing and all channel events from one MP archive.</summary>
    /// <param name="archive">Archive read from the user's disc during preparation.</param>
    /// <returns>Serializable tracks retaining group, local and contiguous identities.</returns>
    public static object Tracks(byte[] archive) => PlayStationSequenceDecoder.DecodeAll(archive)
        .SelectMany((group, index) => group.Tracks.Select(track => new
        {
            group = index,
            number = track.Number,
            ticksPerQuarterNote = track.TicksPerQuarterNote,
            tempoMicrosecondsPerQuarterNote = track.TempoMicrosecondsPerQuarterNote,
            events = SequenceMidiDecoder.Decode(archive, track),
        }));
}
