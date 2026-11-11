using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Dw3.Data;
using Dw3Production.Conversion;
using Dw3Production.Presentation;
using Dw3Production.Simulation;
using Xunit;

namespace Dw3.Game.Tests;

/// <summary>Contract tests for BattleAudioConsumer and BattleAudioManifestLoader using a fabricated pack.
/// No audio hardware is accessed; the DryRunAudioBackend verifies PCM file access only.</summary>
public sealed class BattleAudioConsumerTests : IDisposable
{
    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"dw3-audio-{Guid.NewGuid():N}");

    [Fact]
    public void Consume_SoundRequest_AppliesAndReadsPcm()
    {
        WritePack(programCount: 2, programs: [(ProgramIndex: 0, SampleRef: 1), (ProgramIndex: 1, SampleRef: 2)]);
        var backend = new DryRunAudioBackend();
        BattleAudioConsumer consumer = BuildConsumer(backend);
        var request = new SoundRequestEvent(BattleSide.Player, SoundId: 0, Timing: 0);

        SequenceAcknowledgement result = consumer.Consume(request);

        Assert.Equal(SequenceAcknowledgement.Applied, result);
        DryRunAudioBackend.DryRunVoiceEvent ev = Assert.Single(backend.Events);
        Assert.True(ev.Started);
        Assert.True(ev.ByteCount > 0, "PCM bytes must be observed (non-zero byte count).");
        Assert.Equal(0, ev.SampleIndex);
    }

    [Fact]
    public void Consume_SameRequestTwice_NoDuplicateVoice()
    {
        WritePack(programCount: 1, programs: [(ProgramIndex: 0, SampleRef: 1)]);
        var backend = new DryRunAudioBackend();
        BattleAudioConsumer consumer = BuildConsumer(backend);
        var request = new SoundRequestEvent(BattleSide.Player, SoundId: 0, Timing: 0);

        SequenceAcknowledgement first = consumer.Consume(request);
        SequenceAcknowledgement second = consumer.Consume(request);

        Assert.Equal(SequenceAcknowledgement.Applied, first);
        Assert.Equal(SequenceAcknowledgement.Applied, second);
        Assert.Single(backend.Events, ev => ev.Started);
    }

    [Fact]
    public void Consume_MissingSoundId_ReturnsUnavailable()
    {
        WritePack(programCount: 1, programs: [(ProgramIndex: 0, SampleRef: 1)]);
        var backend = new DryRunAudioBackend();
        BattleAudioConsumer consumer = BuildConsumer(backend);
        var request = new SoundRequestEvent(BattleSide.Player, SoundId: 99, Timing: 0);

        SequenceAcknowledgement result = consumer.Consume(request);

        Assert.Equal(SequenceAcknowledgement.Unavailable, result);
        Assert.Empty(backend.Events);
    }

    [Fact]
    public void Consume_NonSoundEvent_ReturnsUnavailable()
    {
        WritePack(programCount: 1, programs: [(ProgramIndex: 0, SampleRef: 1)]);
        var backend = new DryRunAudioBackend();
        BattleAudioConsumer consumer = BuildConsumer(backend);
        var request = new SequenceEndedEvent(BattleSide.Player, 10, 20);

        SequenceAcknowledgement result = consumer.Consume(request);

        Assert.Equal(SequenceAcknowledgement.Unavailable, result);
        Assert.Empty(backend.Events);
    }

    [Fact]
    public void StopAll_StopsAllActiveVoicesAndAllowsFreshStart()
    {
        WritePack(programCount: 2, programs: [(ProgramIndex: 0, SampleRef: 1), (ProgramIndex: 1, SampleRef: 2)]);
        var backend = new DryRunAudioBackend();
        BattleAudioConsumer consumer = BuildConsumer(backend);
        consumer.Consume(new SoundRequestEvent(BattleSide.Player, SoundId: 0, Timing: 0));
        consumer.Consume(new SoundRequestEvent(BattleSide.Player, SoundId: 1, Timing: 0));
        Assert.Equal(2, backend.Events.Count(ev => ev.Started));

        consumer.StopAll();

        Assert.Equal(2, backend.Events.Count(ev => !ev.Started));

        consumer.Consume(new SoundRequestEvent(BattleSide.Player, SoundId: 0, Timing: 0));
        Assert.Equal(3, backend.Events.Count(ev => ev.Started));
    }

    [Fact]
    public void RegisterAudioConsumer_WiresConsumerAndCancelEffects()
    {
        WritePack(programCount: 1, programs: [(ProgramIndex: 0, SampleRef: 1)]);
        var backend = new DryRunAudioBackend();
        BattleAudioConsumer consumer = BuildConsumer(backend);
        var playback = new AudioTestPlayback();
        playback.RegisterAudioConsumer(consumer);
        playback.Begin(new ActionSequenceContext(BattleSide.Player, 10, 20, [], 5));

        playback.Consume(new SoundRequestEvent(BattleSide.Player, SoundId: 0, Timing: 0));
        playback.Finish(cancelled: true);

        Assert.Single(backend.Events, ev => ev.Started);
        Assert.Single(backend.Events, ev => !ev.Started);
    }

    [Fact]
    public void ManifestLoader_ProducesEntriesForActiveProgramsOnly()
    {
        WritePack(programCount: 2, programs: [(ProgramIndex: 0, SampleRef: 1), (ProgramIndex: 1, SampleRef: 0)]);
        var pack = new DataPackReader(_root);

        IReadOnlyDictionary<int, AudioProgramEntry> entries = BattleAudioManifestLoader.Load(pack);

        Assert.True(entries.ContainsKey(0), "Program 0 should be resolved (sampleRef=1).");
        Assert.False(entries.ContainsKey(1), "Program 1 should be absent (sampleRef=0 is silent).");
        Assert.True(File.Exists(entries[0].WavPath), "WAV path must exist on disk.");
        Assert.Equal(0, entries[0].SampleIndex);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static byte[] BuildMinimalWav(int sampleRate, int samples)
    {
        int dataBytes = samples * 2;
        byte[] wav = new byte[44 + dataBytes];
        Encoding.ASCII.GetBytes("RIFF").CopyTo(wav, 0);
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(4), 36 + dataBytes);
        Encoding.ASCII.GetBytes("WAVE").CopyTo(wav, 8);
        Encoding.ASCII.GetBytes("fmt ").CopyTo(wav, 12);
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(16), 16);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(20), 1);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(22), 1);
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(24), sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(28), sampleRate * 2);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(32), 2);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(34), 16);
        Encoding.ASCII.GetBytes("data").CopyTo(wav, 36);
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(40), dataBytes);
        return wav;
    }

    private static byte[] BuildMpArchive(int programCount, (int ProgramIndex, int SampleRef)[] programs, int sampleCount)
    {
        const int vabHeaderBytes = 32;
        const int programTableBytes = 128 * 16;
        int toneTableBytes = programCount * 16 * 32;
        int sampleLengthTableBytes = 512;
        int vabSize = vabHeaderBytes + programTableBytes + toneTableBytes + sampleLengthTableBytes;
        byte[] vab = new byte[vabSize];

        BinaryPrimitives.WriteUInt32LittleEndian(vab, 0x56414270U);
        BinaryPrimitives.WriteUInt32LittleEndian(vab.AsSpan(4), 7U);
        BinaryPrimitives.WriteUInt32LittleEndian(vab.AsSpan(12), (uint)vabSize);
        BinaryPrimitives.WriteUInt16LittleEndian(vab.AsSpan(18), (ushort)programCount);
        vab[24] = 120;
        vab[25] = 64;

        int toneTableStart = vabHeaderBytes + programTableBytes;
        foreach ((int programIndex, int sampleRef) in programs)
        {
            if (programIndex >= programCount || sampleRef == 0)
            {
                continue;
            }

            int toneOffset = toneTableStart + (programIndex * 16 * 32);
            vab[toneOffset] = 1;
            vab[toneOffset + 2] = 110;
            vab[toneOffset + 3] = 64;
            vab[toneOffset + 4] = 60;
            BinaryPrimitives.WriteInt16LittleEndian(vab.AsSpan(toneOffset + 20, 2), (short)(programIndex + 1));
            BinaryPrimitives.WriteInt16LittleEndian(vab.AsSpan(toneOffset + 22, 2), (short)sampleRef);
        }

        int sampleLengthStart = toneTableStart + toneTableBytes;
        for (int i = 0; i < sampleCount - 1; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(vab.AsSpan(sampleLengthStart + ((i + 1) * 2), 2), 16);
        }

        byte[] archive = new byte[8 + vab.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(archive, 8U);
        vab.CopyTo(archive, 8);
        return archive;
    }

    private static void WriteManifest(string audioDir, int programCount, int sampleCount, string[] wavPaths, byte[] mpArchive, (int ProgramIndex, int SampleRef)[] programEntries)
    {
        var samples = Enumerable.Range(0, sampleCount).Select(i => new
        {
            index = i,
            wavPath = wavPaths[i],
            pcmSamples = 128,
            encodedBytes = 128,
            loops = false,
            loopStartSample = (int?)null,
        });

        var manifest = new
        {
            schema = "battle-audio-vab-v3",
            banks = new[]
            {
                new
                {
                    name = "common",
                    headerPath = "AAA/DAT/SOUND/COMMON/MPCOMMON.BIN",
                    bodyPath = "AAA/DAT/SOUND/COMMON/MVCOMMON.BIN",
                    headerBytes = mpArchive.Length,
                    bodyBytes = 0,
                    headerSha256 = "0000",
                    bodySha256 = "0000",
                    vab = new
                    {
                        Version = 7,
                        Id = 0,
                        FileSize = mpArchive.Length,
                        ProgramCount = programCount,
                        ToneCount = programCount,
                        SampleCount = sampleCount,
                        MasterVolume = 120,
                        MasterPan = 64,
                    },
                    sep = new { offset = 0, length = 0, trackCount = 0, tracks = Array.Empty<object>() },
                    programs = programEntries
                        .Where(p => p.SampleRef > 0 && p.ProgramIndex < programCount)
                        .Select(p => new { index = p.ProgramIndex, sampleIndex = p.SampleRef - 1, volume = 110, pan = 64, centerNote = 60, pitchShift = 0 })
                        .ToArray(),
                    samples,
                },
            },
        };

        File.WriteAllText(
            Path.Combine(audioDir, "manifest.json"),
            JsonSerializer.Serialize(manifest, IndentedJson));
    }

    private BattleAudioConsumer BuildConsumer(IBattleAudioBackend backend)
    {
        var pack = new DataPackReader(_root);
        IReadOnlyDictionary<int, AudioProgramEntry> entries = BattleAudioManifestLoader.Load(pack);
        return new BattleAudioConsumer(entries, backend);
    }

    private void WritePack(int programCount, (int ProgramIndex, int SampleRef)[] programs)
    {
        string audioDir = Path.Combine(_root, "data", "battle", "audio");
        Directory.CreateDirectory(audioDir);

        int sampleCount = programs.Max(p => p.SampleRef) + 1;
        var wavPaths = new string[sampleCount];
        for (int i = 0; i < sampleCount; i++)
        {
            string dir = Path.Combine(audioDir, "common");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, $"{i:D3}.wav");
            File.WriteAllBytes(path, BuildMinimalWav(44100, samples: 128));
            wavPaths[i] = $"battle/audio/common/{i:D3}.wav";
        }

        byte[] mpArchive = BuildMpArchive(programCount, programs, sampleCount);
        WriteManifest(audioDir, programCount, sampleCount, wavPaths, mpArchive, programs);
    }
}
