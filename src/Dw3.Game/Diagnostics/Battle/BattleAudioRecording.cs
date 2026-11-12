using System.Buffers.Binary;
using Godot;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>Opt-in measurement of the engine bus during an actual physical action, never an input WAV copy.</summary>
internal sealed class BattleAudioRecording : IDisposable
{
    private readonly AudioEffectRecord _record = new() { Format = AudioStreamWav.FormatEnum.Format16Bits };
    private readonly string _path;
    private readonly int _effectIndex;

    /// <summary>Initializes a new instance of the <see cref="BattleAudioRecording"/> class.</summary>
    /// <param name="path">Private output file.</param>
    private BattleAudioRecording(string path)
    {
        if (AudioServer.GetDriverName() != "Dummy")
        {
            throw new InvalidOperationException("Action audio recording requires --audio-driver Dummy.");
        }

        _path = path;
        _effectIndex = AudioServer.GetBusEffectCount(0);
        AudioServer.AddBusEffect(0, _record);
        _record.SetRecordingActive(true);
    }

    /// <summary>Gets a value indicating whether actual action audio is required rather than synchronization-only acknowledgements.</summary>
    public static bool Enabled => System.Environment.GetEnvironmentVariable("DW3_ACTION_AUDIO_RECORD") == "1";

    /// <summary>Creates the opt-in recording without changing the physical action or its consumers.</summary>
    /// <param name="path">Private output WAV.</param>
    /// <returns>A recorder, or null when audio evidence was not requested.</returns>
    public static BattleAudioRecording? Start(string path)
        => Enabled ? new(path) : null;

    /// <summary>Saves and requires nonzero engine-mixed PCM from the selected action.</summary>
    public void Save()
    {
        _record.SetRecordingActive(false);
        using var wav = _record.GetRecording();
        byte[] pcm = wav.Data;
        int peak = 0;
        for (int offset = 0; offset < pcm.Length; offset += 2)
        {
            peak = Math.Max(peak, Math.Abs((int)BinaryPrimitives.ReadInt16LittleEndian(pcm.AsSpan(offset, 2))));
        }

        if (wav.SaveToWav(_path) != Error.Ok)
        {
            throw new IOException("Cannot save action mixer recording.");
        }

        GD.Print($"B9061_ACTION_MIX driver=Dummy path={_path} bytes={pcm.Length} peak={peak}");
        if (peak == 0)
        {
            throw new InvalidOperationException("Actual action produced no engine-mixed audio.");
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _record.SetRecordingActive(false);
        AudioServer.RemoveBusEffect(0, _effectIndex);
        _record.Dispose();
    }
}
