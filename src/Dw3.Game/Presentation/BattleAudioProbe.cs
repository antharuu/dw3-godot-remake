using Dw3.Data;
using Dw3Production.Conversion;
using Dw3Production.Simulation;
using Godot;

namespace Dw3Production.Presentation;

/// <summary>Offscreen engine-mix evidence. Run only with the Dummy audio driver.</summary>
public sealed partial class BattleAudioProbe : Node
{
    /// <inheritdoc/>
    public override async void _Ready()
    {
        try
        {
            if (AudioServer.GetDriverName() != "Dummy")
            {
                throw new InvalidOperationException("Audio probe requires --audio-driver Dummy.");
            }

            await Capture();
            await ToSignal(GetTree().CreateTimer(0.2), SceneTreeTimer.SignalName.Timeout);
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PrintErr(error);
            GetTree().Quit(1);
        }
    }

    /// <summary>Records two lifecycle passes through the same renderer, backend and pack provider.</summary>
    /// <returns>Completion of the muted recordings.</returns>
    private async Task Capture()
    {
        string output = System.Environment.GetEnvironmentVariable("DW3_AUDIO_PROOF")
            ?? throw new InvalidOperationException("DW3_AUDIO_PROOF required.");
        Directory.CreateDirectory(output);
        using var renderer = new BattleRenderer(this);
        using var loader = new BattleDataPackLoader(new DataPackReader(UserPaths.GetDataDirectory()));
        var script = new PackActionScriptProvider(loader).TryLoad(26, 5)
            ?? throw new InvalidOperationException("Required production action script missing.");
        var command = script.Commands.First(value => value.Opcode == Dw3.Data.Battle.BattleActionOpcode.Sound);
        for (int cycle = 0; cycle < 2; cycle++)
        {
            renderer.Show();
            await Record(renderer, new SoundRequestEvent(BattleSide.Player, command.Arguments[0], command.Arguments[1]), Path.Combine(output, $"mix-{cycle}.wav"));
            renderer.Hide();
            if (renderer.ActiveAudioVoices != 0)
            {
                throw new InvalidOperationException("Battle teardown retained physical voices.");
            }

            GD.Print($"AUDIO_TEARDOWN cycle={cycle} active={renderer.ActiveAudioVoices} sameRenderer=true sameBackend=true");
            await ToSignal(GetTree().CreateTimer(0.15), SceneTreeTimer.SignalName.Timeout);
            await RecordSilence(Path.Combine(output, $"post-hide-{cycle}.wav"));
        }
    }

    /// <summary>Requires silence on the engine bus after the stopped mixer block has drained.</summary>
    /// <param name="path">Private post-teardown recording.</param>
    /// <returns>Completion of the silent-bus measurement.</returns>
    private async Task RecordSilence(string path)
    {
        using var record = new AudioEffectRecord { Format = AudioStreamWav.FormatEnum.Format16Bits };
        AudioServer.AddBusEffect(0, record);
        record.SetRecordingActive(true);
        await ToSignal(GetTree().CreateTimer(0.2), SceneTreeTimer.SignalName.Timeout);
        record.SetRecordingActive(false);
        using var recording = record.GetRecording();
        recording.SaveToWav(path);
        AudioServer.RemoveBusEffect(0, AudioServer.GetBusEffectCount(0) - 1);
        if (recording.Data.Any(value => value != 0))
        {
            throw new InvalidOperationException("Physical audio remained on the bus after battle hide.");
        }

        GD.Print($"AUDIO_POST_HIDE bytes={recording.Data.Length} peak=0 path={path}");
    }

    /// <summary>Observes the real mixer bus without playing through any host device.</summary>
    /// <param name="renderer">Production mount also used by GameHost.</param>
    /// <param name="request">Sound request taken from a real pack script.</param>
    /// <param name="path">Private output WAV.</param>
    /// <returns>Completion of the recording.</returns>
    private async Task Record(BattleRenderer renderer, SoundRequestEvent request, string path)
    {
        using var record = new AudioEffectRecord { Format = AudioStreamWav.FormatEnum.Format16Bits };
        AudioServer.AddBusEffect(0, record);
        record.SetRecordingActive(true);
        try
        {
            var acknowledgement = renderer.SequencePlayback.Consume(request);
            if (acknowledgement != SequenceAcknowledgement.Applied)
            {
                throw new InvalidOperationException($"Real renderer audio unavailable: {acknowledgement}");
            }

            await ToSignal(GetTree().CreateTimer(0.75), SceneTreeTimer.SignalName.Timeout);
            record.SetRecordingActive(false);
            using var recording = record.GetRecording();
            byte[] pcm = recording.Data;
            if (recording.SaveToWav(path) != Error.Ok)
            {
                throw new IOException("Engine recording could not be saved.");
            }

            int peak = Enumerable.Range(0, pcm.Length / 2)
                .Max(index => Math.Abs((int)System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(pcm.AsSpan(index * 2, 2))));
            GD.Print($"AUDIO_MIX driver={AudioServer.GetDriverName()} bytes={pcm.Length} peak={peak} path={path}");
            if (peak == 0)
            {
                throw new InvalidOperationException("Engine produced silence.");
            }
        }
        finally
        {
            record.SetRecordingActive(false);
            AudioServer.RemoveBusEffect(0, AudioServer.GetBusEffectCount(0) - 1);
        }
    }
}
