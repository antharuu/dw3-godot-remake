using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dw3.Data;
using Dw3.Disc;
using Dw3.Formats.Battle;

namespace Dw3.Extractor;

/// <summary>Extracts the VAB header archives and paired SPU bodies required by battle sound banks.</summary>
internal static class BattleAudioExtractionStep
{
    private const string Domain = "battle-audio";
    private const string OutputSubdirectory = "battle/audio";
    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };
    private static readonly SoundBankSource[] Sources =
    [
        new("common", "AAA/DAT/SOUND/COMMON/MPCOMMON.BIN", "AAA/DAT/SOUND/COMMON/MVCOMMON.BIN"),
        new("battle", "AAA/DAT/SOUND/BATL00/MPBATL00.BIN", "AAA/DAT/SOUND/BATL00/MVBATL00.BIN"),
        new("boss", "AAA/DAT/SOUND/BOSS00/MPBOSS00.BIN", "AAA/DAT/SOUND/BOSS00/MVBOSS00.BIN"),
    ];

    /// <summary>Extracts the three combat-relevant bank pairs and records their decoded VAB headers.</summary>
    /// <param name="openDisc">Opens the supported disc.</param>
    /// <param name="outputDirectory">Root directory for the data pack.</param>
    /// <param name="output">Receives the completion event.</param>
    /// <param name="error">Receives extraction errors.</param>
    /// <returns>Zero on success, otherwise one.</returns>
    internal static int Extract(Func<IDiscSource> openDisc, string outputDirectory, TextWriter output, TextWriter error)
    {
        try
        {
            using var disc = openDisc();
            SupportedEdition edition = disc.IdentifyEdition();
            IReadOnlyList<DiscFileEntry> entries = disc.ListFiles();
            new DataPackWriter(outputDirectory).Write(
                new DataPackMetadata(typeof(BattleAudioExtractionStep).Assembly.GetName().Version?.ToString(3) ?? "0.1.0", edition.BinSha1, edition.Label, DateTimeOffset.UtcNow),
                context => WriteBanks(context, disc, entries));
            output.WriteLine("{\"event\":\"domain-complete\",\"domain\":\"battle-audio\"}");
            return 0;
        }
        catch (Exception ex) when (ex is DiscSourceException or InvalidDataException or FormatException or IOException or ArgumentException)
        {
            error.WriteLine($"Extraction error [{Domain}]: {ex.Message}");
            return 1;
        }
    }

    /// <summary>Reads each supported pair, decodes its header, and writes its private bytes and manifest.</summary>
    /// <param name="context">The data-pack write context.</param>
    /// <param name="disc">The opened source disc.</param>
    /// <param name="entries">All source-disc entries.</param>
    private static void WriteBanks(DataPackWriteContext context, IDiscSource disc, IReadOnlyList<DiscFileEntry> entries)
    {
        var banks = new List<ExtractedSoundBank>();
        foreach (SoundBankSource source in Sources)
        {
            byte[] headerArchive = disc.ReadFile(Require(entries, source.HeaderPath));
            byte[] bodyArchive = disc.ReadFile(Require(entries, source.BodyPath));
            SoundBankHeader header = SoundBankHeaderDecoder.Decode(headerArchive);
            PlayStationSequence sequence = PlayStationSequenceDecoder.Decode(headerArchive);
            IReadOnlyList<DecodedSpuAdpcmSample> samples = SoundBankBodyDecoder.Decode(headerArchive, bodyArchive);
            VabToneAttributes?[] programs = DecodePrograms(headerArchive, header.ProgramCount);
            WriteFile(context, $"{OutputSubdirectory}/{source.Name}.mp", headerArchive);
            WriteFile(context, $"{OutputSubdirectory}/{source.Name}.mv", bodyArchive);
            WriteWaves(context, source.Name, samples);
            banks.Add(new ExtractedSoundBank(source, header, sequence, headerArchive, bodyArchive, samples, programs));
        }

        WriteManifest(context, banks);
    }

    /// <summary>Decodes the first active tone for each program index from the VAB entry in the MP archive.</summary>
    /// <param name="headerArchive">Raw MP archive bytes.</param>
    /// <param name="programCount">Number of programs declared by the VAB header.</param>
    /// <returns>Array of VabToneAttributes indexed by program slot; null means no active tone.</returns>
    private static VabToneAttributes?[] DecodePrograms(byte[] headerArchive, ushort programCount)
    {
        IReadOnlyList<ArchiveRange> archiveEntries = ModelArchive.ReadBounds(headerArchive);
        if (archiveEntries.Count == 0 || archiveEntries[0].Start == archiveEntries[0].End)
        {
            return new VabToneAttributes?[128];
        }

        ArchiveRange vabRange = archiveEntries[0];
        ReadOnlySpan<byte> vabEntry = headerArchive.AsSpan(vabRange.Start, vabRange.End - vabRange.Start);
        return VabProgramTableDecoder.Decode(vabEntry, programCount);
    }

    /// <summary>Writes one extracted proprietary bank member below the data-pack image root.</summary>
    /// <param name="context">The data-pack write context.</param>
    /// <param name="relativePath">Relative destination path.</param>
    /// <param name="bytes">Source bytes to preserve locally.</param>
    private static void WriteFile(DataPackWriteContext context, string relativePath, byte[] bytes)
    {
        string path = context.GetImagePath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }

    /// <summary>Writes each independently decoded VAB body as a local PCM WAV file.</summary>
    /// <param name="context">The data-pack write context.</param>
    /// <param name="bankName">Stable bank name.</param>
    /// <param name="samples">Decoded SPU samples to serialize.</param>
    private static void WriteWaves(DataPackWriteContext context, string bankName, IReadOnlyList<DecodedSpuAdpcmSample> samples)
    {
        for (int index = 0; index < samples.Count; index++)
        {
            byte[] wav = WavePcm16Encoder.EncodeMono(samples[index].PcmSamples, 44100);
            WriteFile(context, $"{OutputSubdirectory}/{bankName}/{index:D3}.wav", wav);
        }
    }

    /// <summary>Writes the source paths, hashes, and decoded VAB metadata for every bank pair.</summary>
    /// <param name="context">The data-pack write context.</param>
    /// <param name="banks">Decoded source pairs.</param>
    private static void WriteManifest(DataPackWriteContext context, IReadOnlyList<ExtractedSoundBank> banks)
    {
        string path = context.GetImagePath($"{OutputSubdirectory}/manifest.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(new { schema = "battle-audio-vab-v3", banks = banks.Select(BankManifest) }, IndentedJson) + "\n", new UTF8Encoding(false));
    }

    /// <summary>Builds one serializable manifest row without retaining a direct disc handle.</summary>
    /// <param name="bank">The decoded bank.</param>
    /// <returns>A JSON-serializable projection.</returns>
    private static object BankManifest(ExtractedSoundBank bank) => new
    {
        name = bank.Source.Name,
        headerPath = bank.Source.HeaderPath,
        bodyPath = bank.Source.BodyPath,
        headerBytes = bank.HeaderArchive.Length,
        bodyBytes = bank.BodyArchive.Length,
        headerSha256 = Convert.ToHexStringLower(SHA256.HashData(bank.HeaderArchive)),
        bodySha256 = Convert.ToHexStringLower(SHA256.HashData(bank.BodyArchive)),
        vab = bank.Header,
        sep = SequenceManifest(bank.Sequence),
        programs = ProgramManifest(bank),
        samples = bank.Samples.Select((sample, index) => new { index, wavPath = $"{OutputSubdirectory}/{bank.Source.Name}/{index:D3}.wav", pcmSamples = sample.PcmSamples.Length, encodedBytes = sample.EncodedBytesConsumed, loops = sample.Loops, loopStartSample = sample.LoopStartSample }),
    };

    /// <summary>Projects the program-to-tone table as a compact array of resolved entries for programs with an active tone and non-zero sample reference.</summary>
    /// <param name="bank">Extracted bank with decoded program tones.</param>
    /// <returns>Enumerable of JSON-serializable program entries.</returns>
    private static IEnumerable<object> ProgramManifest(ExtractedSoundBank bank)
    {
        for (int index = 0; index < bank.Programs.Length; index++)
        {
            VabToneAttributes? tone = bank.Programs[index];
            if (tone is null || tone.SampleRef <= 0)
            {
                continue;
            }

            yield return new
            {
                index,
                sampleIndex = tone.SampleRef - 1,
                volume = tone.Volume,
                pan = tone.Pan,
                centerNote = tone.CenterNote,
                pitchShift = tone.PitchShift,
            };
        }
    }

    /// <summary>Builds one stable SEP metadata projection without retaining event payload bytes.</summary>
    /// <param name="sequence">Validated SEP structural metadata.</param>
    /// <returns>A JSON-serializable projection containing offsets, timing, and event counts only.</returns>
    private static object SequenceManifest(PlayStationSequence sequence) => new
    {
        offset = sequence.Offset,
        length = sequence.Length,
        trackCount = sequence.Tracks.Count,
        tracks = sequence.Tracks.Select(TrackManifest),
    };

    /// <summary>Builds one stable metadata projection for a SEP track.</summary>
    /// <param name="track">Validated track metadata and safe event summary.</param>
    /// <returns>A JSON-serializable projection without source event contents.</returns>
    private static object TrackManifest(PlayStationSequenceTrack track) => new
    {
        number = track.Number,
        headerOffset = track.HeaderOffset,
        eventsOffset = track.EventsOffset,
        endOffset = track.EndOffset,
        ticksPerQuarterNote = track.TicksPerQuarterNote,
        tempoMicrosecondsPerQuarterNote = track.TempoMicrosecondsPerQuarterNote,
        events = new
        {
            eventCount = track.Events.EventCount,
            channelEventCount = track.Events.ChannelEventCount,
            metaEventCount = track.Events.MetaEventCount,
            systemEventCount = track.Events.SystemEventCount,
            totalDeltaTicks = track.Events.TotalDeltaTicks,
            hasEndOfTrack = track.Events.HasEndOfTrack,
        },
    };

    /// <summary>Finds a required ISO member by its canonical path.</summary>
    /// <param name="entries">All available disc entries.</param>
    /// <param name="path">The required canonical ISO path.</param>
    /// <returns>The matching disc entry.</returns>
    private static DiscFileEntry Require(IReadOnlyList<DiscFileEntry> entries, string path) =>
        entries.SingleOrDefault(entry => entry.IsoPath == path) ?? throw new InvalidDataException($"Required disc member missing: {path}");

    /// <summary>Canonical ISO members forming one VAB header and SPU body pair.</summary>
    private sealed record SoundBankSource(string Name, string HeaderPath, string BodyPath);

    /// <summary>Decoded metadata and retained bytes for one source pair while the data pack is written.</summary>
    private sealed record ExtractedSoundBank(
        SoundBankSource Source,
        SoundBankHeader Header,
        PlayStationSequence Sequence,
        byte[] HeaderArchive,
        byte[] BodyArchive,
        IReadOnlyList<DecodedSpuAdpcmSample> Samples,
        VabToneAttributes?[] Programs);
}
