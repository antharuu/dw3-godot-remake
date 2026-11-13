using Dw3Production.Conversion;
using Godot;

namespace Dw3Production.Presentation;

/// <summary>
/// Smallest falsifying experiment for the physical reverb consumer. A fabricated 50 ms tone is driven
/// through authored reverb commands, the production sequence consumer and the production backend,
/// and the engine mix is recorded. A dry voice, depth zero and type off must leave no tail, depth 56
/// and 127 must leave growing tails, and a live depth change must silence a ringing tail.
/// </summary>
public sealed partial class BattleReverbProbe : Node
{
    private const int MixRate = 44100;
    private const int BurstFrames = 2205;
    private const int DryFrames = BurstFrames + 441;

    /// <inheritdoc/>
    public override async void _Ready()
    {
        try
        {
            if (AudioServer.GetDriverName() != "Dummy")
            {
                throw new InvalidOperationException("Only the Dummy driver is permitted for this probe.");
            }

            await RunAsync();
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PrintErr(error);
            GetTree().Quit(1);
        }
    }

    /// <summary>Writes the fabricated excitation: a 50 ms 440 Hz mono tone.</summary>
    /// <param name="directory">Private output directory.</param>
    /// <returns>Path of the excitation WAV.</returns>
    private static string WriteExcitation(string directory)
    {
        byte[] data = new byte[BurstFrames * 2];
        for (int frame = 0; frame < BurstFrames; frame++)
        {
            short value = (short)(16000 * Math.Sin(2 * Math.PI * 440 * frame / MixRate));
            System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(frame * 2, 2), value);
        }

        string path = Path.Combine(directory, "excitation.wav");
        using var wav = new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = MixRate, Stereo = false, Data = data };
        return wav.SaveToWav(path) == Error.Ok ? path : throw new IOException("Cannot write the excitation.");
    }

    /// <summary>Builds an authored sequence that sets the global reverb, then optionally keys one note on.</summary>
    /// <param name="type">Source reverb preset.</param>
    /// <param name="depth">Source reverb depth.</param>
    /// <param name="note">Note to start at time zero, after the controls, or null.</param>
    /// <returns>A prepared sequence for the production consumer.</returns>
    private static PreparedAudioSequence Authored(int type, int depth, AudioProgramEntry? note)
    {
        List<AudioSequenceCommand> commands =
        [
            new(0, 0, 0, null, Reverb: new AudioReverbChange(AudioReverbParameter.Type, type)),
            new(0, 0, 0, null, Reverb: new AudioReverbChange(AudioReverbParameter.Depth, depth)),
        ];
        if (note is not null)
        {
            commands.Add(new AudioSequenceCommand(0, 0, 60, note));
        }

        return new PreparedAudioSequence(commands, 0.06);
    }

    /// <summary>Requires a measured property and reports it.</summary>
    /// <param name="condition">Measured property.</param>
    /// <param name="message">Failure explanation.</param>
    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    /// <summary>Runs every case on one production backend and proves its cleanup.</summary>
    /// <returns>Completion of all measurements.</returns>
    private async Task RunAsync()
    {
        string output = System.Environment.GetEnvironmentVariable("DW3_AUDIO_PROOF") ?? throw new InvalidOperationException("DW3_AUDIO_PROOF required.");
        Directory.CreateDirectory(output);
        var wet = new AudioProgramEntry(0, WriteExcitation(output), 127, 64, 60, 0) { Reverb = true };
        string bus;
        using (var backend = new GodotBattleAudioBackend(this))
        {
            bus = backend.Reverb.Name;
            var dry = await CaseAsync(backend, output, "dry-type3-depth127", Authored(3, 127, wet with { Reverb = false }));
            var zero = await CaseAsync(backend, output, "wet-type3-depth0", Authored(3, 0, wet));
            var off = await CaseAsync(backend, output, "wet-type0-depth127", Authored(0, 127, wet));
            var mid = await CaseAsync(backend, output, "wet-type3-depth56", Authored(3, 56, wet));
            var full = await CaseAsync(backend, output, "wet-type3-depth127", Authored(3, 127, wet));
            Require(dry.OnsetFrame >= 0 && zero.OnsetFrame >= 0 && off.OnsetFrame >= 0, "An excitation did not reach the mix.");
            Require(dry.TailPeak == 0, "Dry voice received reverb: the state is not voice-scoped by the tone mode.");
            Require(zero.TailPeak == 0 && off.TailPeak == 0, "Depth zero or type off left a tail.");
            Require(mid.TailPeak > 0 && full.TailEnergy > mid.TailEnergy, "Depth does not drive a growing physical tail.");
            await DepthChangeAsync(backend, output, wet);
            await FinalSilenceAsync(backend, output);
        }

        Require(AudioServer.GetBusIndex(bus) < 0, "Reverb bus survived backend disposal.");
        GD.Print($"REVERB_PROBE_PASS driver={AudioServer.GetDriverName()} cases=6 busRemoved=true");
    }

    /// <summary>Records one authored case and drains its tail before the next case.</summary>
    /// <param name="backend">Shared production backend.</param>
    /// <param name="output">Private output directory.</param>
    /// <param name="name">Case name.</param>
    /// <param name="sequence">Authored controls and note.</param>
    /// <returns>The measured tail.</returns>
    private async Task<ReverbTailMeasurement> CaseAsync(GodotBattleAudioBackend backend, string output, string name, PreparedAudioSequence sequence)
    {
        byte[] pcm = await RecordAsync(Path.Combine(output, name + ".wav"), 2.5, () => _ = new BattleSequenceVoice(sequence, backend));
        var result = ReverbTailMeasurement.Measure(pcm, DryFrames);
        backend.StopAll();
        Require(backend.ActiveCount == 0, "Voice pool retained channels after StopAll.");
        GD.Print($"REVERB_CASE name={name} type={backend.Reverb.Type} depth={backend.Reverb.Depth} wet={backend.Reverb.Wet:0.000} onset={result.OnsetFrame} tailPeak={result.TailPeak} tailEnergy={result.TailEnergy:0.000000} tailMs={result.TailFrames * 1000.0 / MixRate:0.0}");
        await WaitAsync(1.0);
        return result;
    }

    /// <summary>Proves that a live depth change acts on an already ringing tail.</summary>
    /// <param name="backend">Shared production backend.</param>
    /// <param name="output">Private output directory.</param>
    /// <param name="wet">Reverb-enabled excitation.</param>
    /// <returns>Completion of the measurement.</returns>
    private async Task DepthChangeAsync(GodotBattleAudioBackend backend, string output, AudioProgramEntry wet)
    {
        byte[] ringing = await RecordAsync(Path.Combine(output, "depth-change-before.wav"), 0.15, () => _ = new BattleSequenceVoice(Authored(3, 127, wet), backend));
        _ = new BattleSequenceVoice(new PreparedAudioSequence([new(0, 0, 0, null, Reverb: new AudioReverbChange(AudioReverbParameter.Depth, 0))], 0.01), backend);
        await WaitAsync(0.05);
        byte[] after = await RecordAsync(Path.Combine(output, "depth-change-after.wav"), 0.6, () => { });
        int before = ReverbTailMeasurement.Measure(ringing, DryFrames).TailPeak;
        int cut = ReverbTailMeasurement.Measure(after, 0).TailPeak;
        GD.Print($"REVERB_DEPTH_CHANGE beforePeak={before} afterPeak={cut} afterBytes={after.Length}");
        Require(before > 0 && after.Length > 0 && cut == 0, "A live depth change did not act on the ringing tail.");
        backend.StopAll();
    }

    /// <summary>Lets the last tail drain with reverb still enabled, then records a nonempty silent mix.</summary>
    /// <param name="backend">Shared production backend.</param>
    /// <param name="output">Private output directory.</param>
    /// <returns>Completion of the silence control.</returns>
    private async Task FinalSilenceAsync(GodotBattleAudioBackend backend, string output)
    {
        _ = new BattleSequenceVoice(Authored(3, 127, null), backend);
        await WaitAsync(2.5);
        byte[] silence = await RecordAsync(Path.Combine(output, "final-silence.wav"), 0.5, () => { });
        int peak = ReverbTailMeasurement.Measure(silence, 0).TailPeak;
        GD.Print($"REVERB_FINAL bytes={silence.Length} peak={peak} channels={backend.ActiveCount}");
        Require(silence.Length > 0 && peak == 0 && backend.ActiveCount == 0, "Final mix is not a nonempty silent control.");
    }

    /// <summary>Records the master mix around one action.</summary>
    /// <param name="path">Private WAV path.</param>
    /// <param name="seconds">Recording length.</param>
    /// <param name="action">Action started once recording is active.</param>
    /// <returns>Recorded interleaved stereo PCM16.</returns>
    private async Task<byte[]> RecordAsync(string path, double seconds, Action action)
    {
        using var record = new AudioEffectRecord { Format = AudioStreamWav.FormatEnum.Format16Bits };
        int index = AudioServer.GetBusEffectCount(0);
        AudioServer.AddBusEffect(0, record);
        try
        {
            record.SetRecordingActive(true);
            action();
            await WaitAsync(seconds);
            record.SetRecordingActive(false);
            using var wav = record.GetRecording();
            Require(wav.SaveToWav(path) == Error.Ok, "Cannot save the mixer recording.");
            return wav.Data;
        }
        finally
        {
            record.SetRecordingActive(false);
            AudioServer.RemoveBusEffect(0, index);
        }
    }

    /// <summary>Waits on the scene clock.</summary>
    /// <param name="seconds">Duration.</param>
    /// <returns>Completion.</returns>
    private async Task WaitAsync(double seconds) => await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
}
