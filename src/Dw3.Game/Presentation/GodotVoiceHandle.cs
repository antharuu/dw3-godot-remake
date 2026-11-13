using Dw3Production.Conversion;
using Godot;

namespace Dw3Production.Presentation;

/// <summary>One physical mixer voice, released exactly once on completion or cancellation.</summary>
internal sealed class GodotVoiceHandle : IVoiceHandle
{
    private readonly AudioStreamPlayer _player;
    private readonly AudioStreamWav _stream;
    private readonly SpuEnvelope? _envelope;
    private readonly float _gain;
    private readonly float _basePitch;
    private bool _released;
    private float _volumeScale = 1;

    /// <summary>Initializes a new instance of the <see cref="GodotVoiceHandle"/> class.</summary>
    /// <param name="player">Player owned by the battle node.</param>
    /// <param name="stream">Prepared sample retained until completion.</param>
    /// <param name="entry">Original decoded envelope registers, when available.</param>
    public GodotVoiceHandle(AudioStreamPlayer player, AudioStreamWav stream, AudioProgramEntry? entry = null)
    {
        _player = player;
        _stream = stream;
        _gain = player.VolumeLinear;
        _basePitch = player.PitchScale;
        if (entry?.Adsr1 is { } first && entry.Adsr2 is { } second)
        {
            _envelope = new SpuEnvelope(first, second);
            _player.VolumeLinear = 0;
        }
    }

    /// <inheritdoc/>
    public bool IsComplete { get; private set; }

    /// <inheritdoc/>
    public float EnvelopeLevel => _envelope is null ? 1 : _envelope.Level / 32767f;

    /// <inheritdoc/>
    public void Advance(int ticks)
    {
        if (IsComplete || _envelope is null)
        {
            return;
        }

        _envelope.AdvanceSamples(checked(ticks * 735));

        _player.VolumeLinear = _gain * EnvelopeLevel * _volumeScale;
        if (_envelope.IsComplete)
        {
            Stop();
        }
    }

    /// <inheritdoc/>
    public bool KeyOff()
    {
        if (_envelope is null)
        {
            return false;
        }

        if (!_released)
        {
            _envelope.KeyOff();
            _released = true;
        }

        return true;
    }

    /// <inheritdoc/>
    public void SetVolumeScale(float scale)
    {
        _volumeScale = Math.Clamp(scale, 0f, 1f);
        if (!IsComplete)
        {
            _player.VolumeLinear = _gain * EnvelopeLevel * _volumeScale;
        }
    }

    /// <inheritdoc/>
    public void SetPitchBend(float semitones)
    {
        if (!IsComplete)
        {
            _player.PitchScale = _basePitch * MathF.Pow(2, semitones / 12f);
        }
    }

    /// <summary>Releases the engine stream and player without changing any device configuration.</summary>
    public void Stop()
    {
        if (IsComplete)
        {
            return;
        }

        IsComplete = true;
        _player.Finished -= Stop;
        _player.Stop();
        _player.Stream = null;
        _player.QueueFree();
        _stream.Dispose();
    }
}
