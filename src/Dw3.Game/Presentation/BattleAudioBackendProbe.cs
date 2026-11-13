using Dw3Production.Conversion;
using Godot;

namespace Dw3Production.Presentation;

/// <summary>Measures production mixer gain, pan, pitch, loops and timed key-off using a synthetic input.</summary>
public sealed partial class BattleAudioBackendProbe : Node
{
    /// <inheritdoc/>
    public override async void _Ready()
    {
        try
        {
            if (AudioServer.GetDriverName() != "Dummy")
            {
                throw new InvalidOperationException("Only the Dummy driver is permitted for this probe.");
            }

            await RunCases();
            await ToSignal(GetTree().CreateTimer(0.2), SceneTreeTimer.SignalName.Timeout);
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PrintErr(error);
            GetTree().Quit(1);
        }
    }

    /// <summary>Reuses one real backend for every recording and cleanup.</summary>
    /// <returns>Completion of acoustic test cases.</returns>
    private async Task RunCases()
    {
        string output = System.Environment.GetEnvironmentVariable("DW3_AUDIO_PROOF")
            ?? throw new InvalidOperationException("DW3_AUDIO_PROOF required.");
        string input = System.Environment.GetEnvironmentVariable("DW3_AUDIO_SIGNAL")
            ?? throw new InvalidOperationException("DW3_AUDIO_SIGNAL required.");
        Directory.CreateDirectory(output);
        using var backend = new GodotBattleAudioBackend(this);
        var entry = new AudioProgramEntry(0, input, 127, 64, 60, 0);
        await Record(backend, entry, Path.Combine(output, "natural.wav"));
        await Record(backend, entry with { Pan = 0, Volume = 64, Note = 72 }, Path.Combine(output, "left-half-octave.wav"));
        await Record(backend, entry with { Pan = 127, Loops = true, LoopStart = 8820 }, Path.Combine(output, "right-loop.wav"));
        await Record(backend, entry with { Loops = true, LoopStart = 8820, DurationFrames = 12 }, Path.Combine(output, "timed.wav"));
        await EnvelopeAllocation(backend, entry, Path.Combine(output, "priority-envelope.wav"));
        GD.Print("AUDIO_BACKEND_PROBE cases=5 sameBackend=true postCleanupChannels=0");
    }

    /// <summary>Qualifies equal-priority envelope eviction on all 24 physical mixer channels.</summary>
    /// <param name="backend">The same production backend as the acoustic cases.</param>
    /// <param name="input">Nonzero fabricated signal.</param>
    /// <param name="path">Private engine recording.</param>
    /// <returns>Completion after actual preemption and cleanup.</returns>
    private async Task EnvelopeAllocation(GodotBattleAudioBackend backend, AudioProgramEntry input, string path)
    {
        using var record = new AudioEffectRecord { Format = AudioStreamWav.FormatEnum.Format16Bits };
        AudioServer.AddBusEffect(0, record);
        record.SetRecordingActive(true);
        try
        {
            var entry = input with { Priority = 10, Volume = 4, Loops = true, Adsr1 = 15, Adsr2 = 0x1FC0 };
            var voices = Enumerable.Range(0, 24).Select(index => backend.Play(index == 7 ? entry with { Adsr1 = 0x500F } : entry)).ToArray();
            await ToSignal(GetTree().CreateTimer(0.15), SceneTreeTimer.SignalName.Timeout);
            float quiet = voices[7].EnvelopeLevel;
            float oldest = voices[0].EnvelopeLevel;
            backend.Play(entry);
            if (!voices[7].IsComplete || voices[0].IsComplete || quiet >= oldest || backend.ActiveCount != 24)
            {
                throw new InvalidOperationException("Real backend did not evict the quieter equal-priority envelope.");
            }

            try
            {
                backend.Play(entry with { Priority = 9 });
                throw new InvalidOperationException("Lower priority interrupted a protected physical voice.");
            }
            catch (IOException)
            {
                GD.Print("AUDIO_PRIORITY lower-priority=refused");
            }

            await ToSignal(GetTree().CreateTimer(0.2), SceneTreeTimer.SignalName.Timeout);
            record.SetRecordingActive(false);
            using var recording = record.GetRecording();
            if (recording.SaveToWav(path) != Error.Ok || !recording.Data.Any(value => value != 0))
            {
                throw new InvalidOperationException("Physical envelope-allocation recording is empty or silent.");
            }

            GD.Print($"AUDIO_PRIORITY channels={backend.ActiveCount} quiet={quiet} oldest={oldest} evicted=7 oldest-survives=true path={path}");
        }
        finally
        {
            backend.StopAll();
            record.SetRecordingActive(false);
            AudioServer.RemoveBusEffect(0, AudioServer.GetBusEffectCount(0) - 1);
        }
    }

    /// <summary>Captures engine output while observing natural and forced voice termination.</summary>
    /// <param name="backend">One persistent production backend.</param>
    /// <param name="entry">Synthetic playback attributes under measurement.</param>
    /// <param name="path">Private engine recording.</param>
    /// <returns>Completion of one recording and cleanup.</returns>
    private async Task Record(GodotBattleAudioBackend backend, AudioProgramEntry entry, string path)
    {
        using var record = new AudioEffectRecord { Format = AudioStreamWav.FormatEnum.Format16Bits };
        AudioServer.AddBusEffect(0, record);
        record.SetRecordingActive(true);
        try
        {
            IVoiceHandle handle = backend.Play(entry);
            await ToSignal(GetTree().CreateTimer(1.8), SceneTreeTimer.SignalName.Timeout);
            record.SetRecordingActive(false);
            using var recording = record.GetRecording();
            if (recording.SaveToWav(path) != Error.Ok)
            {
                throw new IOException("Could not save mixer recording.");
            }

            bool expectedComplete = !entry.Loops || entry.DurationFrames > 0;
            if (handle.IsComplete != expectedComplete)
            {
                throw new InvalidOperationException($"Unexpected physical voice completion: {path}");
            }

            backend.StopAll();
            if (backend.ActiveCount != 0)
            {
                throw new InvalidOperationException("Voice pool retained channels after StopAll.");
            }

            GD.Print($"AUDIO_BACKEND_CASE path={path} complete={handle.IsComplete} channels={backend.ActiveCount}");
        }
        finally
        {
            record.SetRecordingActive(false);
            AudioServer.RemoveBusEffect(0, AudioServer.GetBusEffectCount(0) - 1);
        }
    }
}
