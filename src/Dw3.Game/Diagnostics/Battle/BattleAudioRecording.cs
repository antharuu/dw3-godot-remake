using System.Buffers.Binary;
using Godot;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>Opt-in measurement of the engine bus during an actual physical action, never an input WAV copy.</summary>
internal sealed class BattleAudioRecording : IDisposable
{
    private readonly AudioEffectRecord _record = new() { Format = AudioStreamWav.FormatEnum.Format16Bits };
    private readonly AudioEffectCapture _capture = new();
    private readonly string _path;
    private readonly int _effectIndex;
    private bool _hasSignal;

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
        AudioServer.AddBusEffect(0, _capture);
        _record.SetRecordingActive(true);
    }

    /// <summary>Gets a value indicating whether actual action audio is required rather than synchronization-only acknowledgements.</summary>
    public static bool Enabled => System.Environment.GetEnvironmentVariable("DW3_ACTION_AUDIO_RECORD") == "1";

    /// <summary>Gets a value indicating whether the mixer has actually delivered nonzero PCM.</summary>
    public bool HasSignal
    {
        get
        {
            int available = _capture.GetFramesAvailable();
            if (available > 0)
            {
                _hasSignal |= _capture.GetBuffer(available).Any(value => value.X != 0 || value.Y != 0);
            }

            return _hasSignal;
        }
    }

    /// <summary>Creates the opt-in recording without changing the physical action or its consumers.</summary>
    /// <param name="path">Private output WAV.</param>
    /// <returns>A recorder, or null when audio evidence was not requested.</returns>
    public static BattleAudioRecording? Start(string path)
        => Enabled ? new(path) : null;

    /// <summary>Saves the measured bus and requires the requested audible or silent control.</summary>
    /// <param name="expectAudio">True for useful action output, false for a silent cleanup control.</param>
    public void Save(bool expectAudio = true)
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
        if (pcm.Length == 0 || (peak > 0) != expectAudio)
        {
            throw new InvalidOperationException($"Unexpected engine mix: bytes={pcm.Length} peak={peak} expectAudio={expectAudio}.");
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _record.SetRecordingActive(false);
        AudioServer.RemoveBusEffect(0, _effectIndex + 1);
        AudioServer.RemoveBusEffect(0, _effectIndex);
        _capture.Dispose();
        _record.Dispose();
    }
}
