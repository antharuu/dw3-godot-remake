using Godot;

namespace Dw3Production.Presentation;

/// <summary>One physical mixer voice, released exactly once on completion or cancellation.</summary>
internal sealed class GodotVoiceHandle : IVoiceHandle
{
    private readonly AudioStreamPlayer _player;
    private readonly AudioStreamWav _stream;

    /// <summary>Initializes a new instance of the <see cref="GodotVoiceHandle"/> class.</summary>
    /// <param name="player">Player owned by the battle node.</param>
    /// <param name="stream">Prepared sample retained until completion.</param>
    public GodotVoiceHandle(AudioStreamPlayer player, AudioStreamWav stream)
    {
        _player = player;
        _stream = stream;
    }

    /// <inheritdoc/>
    public bool IsComplete { get; private set; }

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
