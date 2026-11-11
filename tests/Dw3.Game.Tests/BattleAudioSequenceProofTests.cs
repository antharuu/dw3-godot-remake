using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Dw3.Data;
using Dw3.Data.Battle;
using Dw3.Extractor;
using Dw3Production;
using Dw3Production.Conversion;
using Dw3Production.Presentation;
using Dw3Production.Simulation;
using Microsoft.Data.Sqlite;
using Xunit;
using TechData = Dw3Production.Simulation.TechData;

namespace Dw3.Game.Tests;

/// <summary>Exercises the production runner→pack→SoundRequestEvent→BattleAudioConsumer path
/// with fabricated action scripts that include Sound opcodes.
/// No audio hardware is accessed; a dry-run backend records PCM file access.</summary>
public sealed class BattleAudioSequenceProofTests : IDisposable
{
    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"dw3-audio-seq-{Guid.NewGuid():N}");

    [Fact]
    public void ProductionRunner_SoundOpcode_AppliesAndRecordsPcm()
    {
        WritePack();
        using var loader = new BattleDataPackLoader(new DataPackReader(_root));
        var flow = BattleFlowActionSequenceTests.BuildFlowOnPlayerTurn();
        flow.PlayerBasicTech = new TechData { Script = 5, Accuracy = 100, Power = 100 };
        var backend = new DryRunAudioBackend();
        IReadOnlyDictionary<int, AudioProgramEntry> programs =
            BattleAudioManifestLoader.Load(new DataPackReader(_root));
        var audio = new BattleAudioConsumer(programs, backend);
        var playback = new AudioTestPlayback { Ready = true };
        playback.RegisterAudioConsumer(audio);
        var runner = new BattleSequenceRunner(flow, new PackActionScriptProvider(loader));
        runner.SetPlayback(playback);
        flow.Choose(BattleCommand.Attack, 0, new FixedRng());

        runner.Tick();

        DryRunAudioBackend.DryRunVoiceEvent ev = Assert.Single(backend.Events, e => e.Started);
        Assert.True(ev.ByteCount > 0, "PCM bytes observed must be non-zero.");
        Assert.Equal(0, ev.SampleIndex);
    }

    [Fact]
    public void PendingRetry_SameSoundId_NoDuplicateVoice()
    {
        WritePack();
        using var loader = new BattleDataPackLoader(new DataPackReader(_root));
        var flow = BattleFlowActionSequenceTests.BuildFlowOnPlayerTurn();
        flow.PlayerBasicTech = new TechData { Script = 5, Accuracy = 100, Power = 100 };
        var backend = new DryRunAudioBackend();
        IReadOnlyDictionary<int, AudioProgramEntry> programs =
            BattleAudioManifestLoader.Load(new DataPackReader(_root));
        var audio = new BattleAudioConsumer(programs, backend);
        var playback = new AudioTestPlayback();
        playback.RegisterAudioConsumer(audio);
        var runner = new BattleSequenceRunner(flow, new PackActionScriptProvider(loader));
        runner.SetPlayback(playback);
        flow.Choose(BattleCommand.Attack, 0, new FixedRng());

        runner.Tick();
        runner.Tick();
        runner.Tick();

        int started = backend.Events.Count(ev => ev.Started);
        Assert.Equal(1, started);
    }

    [Fact]
    public void Finish_CancelledAction_StopsAllVoices()
    {
        WritePack();
        using var loader = new BattleDataPackLoader(new DataPackReader(_root));
        var flow = BattleFlowActionSequenceTests.BuildFlowOnPlayerTurn();
        flow.PlayerBasicTech = new TechData { Script = 5, Accuracy = 100, Power = 100 };
        var backend = new DryRunAudioBackend();
        IReadOnlyDictionary<int, AudioProgramEntry> programs =
            BattleAudioManifestLoader.Load(new DataPackReader(_root));
        var audio = new BattleAudioConsumer(programs, backend);
        var playback = new AudioTestPlayback { Ready = true };
        playback.RegisterAudioConsumer(audio);
        var runner = new BattleSequenceRunner(flow, new PackActionScriptProvider(loader));
        runner.SetPlayback(playback);
        flow.Choose(BattleCommand.Attack, 0, new FixedRng());
        runner.Tick();
        Assert.Single(backend.Events, ev => ev.Started);

        runner.Cancel();

        Assert.Single(backend.Events, ev => !ev.Started);
    }

    [Fact]
    public void SecondCombat_FreshStart_NoResidualVoices()
    {
        WritePack();
        using var loader = new BattleDataPackLoader(new DataPackReader(_root));
        IReadOnlyDictionary<int, AudioProgramEntry> programs =
            BattleAudioManifestLoader.Load(new DataPackReader(_root));

        var backend1 = new DryRunAudioBackend();
        var audio1 = new BattleAudioConsumer(programs, backend1);
        var playback1 = new AudioTestPlayback { Ready = true };
        playback1.RegisterAudioConsumer(audio1);
        var flow1 = BattleFlowActionSequenceTests.BuildFlowOnPlayerTurn();
        flow1.PlayerBasicTech = new TechData { Script = 5, Accuracy = 100, Power = 100 };
        var runner1 = new BattleSequenceRunner(flow1, new PackActionScriptProvider(loader));
        runner1.SetPlayback(playback1);
        flow1.Choose(BattleCommand.Attack, 0, new FixedRng());
        runner1.Tick();
        runner1.Cancel();

        var backend2 = new DryRunAudioBackend();
        var audio2 = new BattleAudioConsumer(programs, backend2);
        var playback2 = new AudioTestPlayback { Ready = true };
        playback2.RegisterAudioConsumer(audio2);
        var flow2 = BattleFlowActionSequenceTests.BuildFlowOnPlayerTurn();
        flow2.PlayerBasicTech = new TechData { Script = 5, Accuracy = 100, Power = 100 };
        var runner2 = new BattleSequenceRunner(flow2, new PackActionScriptProvider(loader));
        runner2.SetPlayback(playback2);
        flow2.Choose(BattleCommand.Attack, 0, new FixedRng());

        runner2.Tick();

        Assert.Single(backend2.Events, ev => ev.Started);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static byte[] BuildMinimalWav()
    {
        const int samples = 128;
        const int dataBytes = samples * 2;
        byte[] wav = new byte[44 + dataBytes];
        Encoding.ASCII.GetBytes("RIFF").CopyTo(wav, 0);
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(4), 36 + dataBytes);
        Encoding.ASCII.GetBytes("WAVE").CopyTo(wav, 8);
        Encoding.ASCII.GetBytes("fmt ").CopyTo(wav, 12);
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(16), 16);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(20), 1);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(22), 1);
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(24), 44100);
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(28), 88200);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(32), 2);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(34), 16);
        Encoding.ASCII.GetBytes("data").CopyTo(wav, 36);
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(40), dataBytes);
        return wav;
    }

    private static byte[] BuildMpArchive()
    {
        const int programCount = 1;
        const int vabHeaderBytes = 32;
        const int programTableBytes = 128 * 16;
        const int toneTableBytes = programCount * 16 * 32;
        int vabSize = vabHeaderBytes + programTableBytes + toneTableBytes + 512;
        byte[] vab = new byte[vabSize];
        BinaryPrimitives.WriteUInt32LittleEndian(vab, 0x56414270U);
        BinaryPrimitives.WriteUInt32LittleEndian(vab.AsSpan(4), 7U);
        BinaryPrimitives.WriteUInt32LittleEndian(vab.AsSpan(12), (uint)vabSize);
        BinaryPrimitives.WriteUInt16LittleEndian(vab.AsSpan(18), programCount);
        vab[24] = 120;
        vab[25] = 64;
        int toneOff = vabHeaderBytes + programTableBytes;
        vab[toneOff] = 1;
        vab[toneOff + 2] = 110;
        vab[toneOff + 3] = 64;
        vab[toneOff + 4] = 60;
        BinaryPrimitives.WriteInt16LittleEndian(vab.AsSpan(toneOff + 20, 2), 1);
        BinaryPrimitives.WriteInt16LittleEndian(vab.AsSpan(toneOff + 22, 2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(vab.AsSpan(toneOff + toneTableBytes + 2, 2), 16);
        byte[] archive = new byte[8 + vab.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(archive, 8U);
        vab.CopyTo(archive, 8);
        return archive;
    }

    private static void WriteManifest(string audioDir, byte[] mpArchive)
    {
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
                    vab = new { Version = 7, Id = 0, FileSize = mpArchive.Length, ProgramCount = 1, ToneCount = 1, SampleCount = 1, MasterVolume = 120, MasterPan = 64 },
                    sep = new { offset = 0, length = 0, trackCount = 0, tracks = Array.Empty<object>() },
                    programs = new[] { new { index = 0, sampleIndex = 0, volume = 110, pan = 64, centerNote = 60, pitchShift = 0 } },
                    samples = new[]
                    {
                        new { index = 0, wavPath = "battle/audio/common/000.wav", pcmSamples = 128, encodedBytes = 128, loops = false, loopStartSample = (int?)null },
                    },
                },
            },
        };
        File.WriteAllText(
            Path.Combine(audioDir, "manifest.json"),
            JsonSerializer.Serialize(manifest, IndentedJson));
    }

    private static void WriteAudioPackToContext(DataPackWriteContext context)
    {
        string audioDir = Path.GetDirectoryName(context.GetImagePath("battle/audio/manifest.json"))!;
        string commonDir = Path.Combine(audioDir, "common");
        Directory.CreateDirectory(commonDir);
        File.WriteAllBytes(Path.Combine(commonDir, "000.wav"), BuildMinimalWav());
        byte[] mp = BuildMpArchive();
        WriteManifest(audioDir, mp);
    }

    private void WritePack()
    {
        new DataPackWriter(_root).Write(
            new DataPackMetadata("test", "synthetic", SyntheticDataPackFixture.SyntheticEdition, DateTimeOffset.UnixEpoch),
            context =>
            {
                WriteAudioPackToContext(context);
                BattleActionDomainWriter.Write(
                    context,
                    [
                        new BattleActionExtraction(
                            10,
                            1,
                            0,
                            5,
                            new BattleActionScript(
                                5,
                                [
                                    new(BattleActionOpcode.Sound, [0, 0]),
                                    new(BattleActionOpcode.Model, [0, 0, 7]),
                                    new(BattleActionOpcode.Model, [0, 0, 0]),
                                    new(BattleActionOpcode.HitDispatch, [0]),
                                    new(BattleActionOpcode.End, []),
                                ]),
                            null),
                    ]);
            });
    }

    private sealed class FixedRng : IBattleRng
    {
        /// <inheritdoc/>
        public int NextValue() => 0;
    }
}
