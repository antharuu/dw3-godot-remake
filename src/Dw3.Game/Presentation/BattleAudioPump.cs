using Godot;

namespace Dw3Production.Presentation;

/// <summary>Advances physical note deadlines even when an action has already ended.</summary>
internal sealed partial class BattleAudioPump : Node
{
    private readonly BattleSourceClock _clock = new();
    private ulong _lastMicroseconds = Time.GetTicksUsec();

    /// <summary>Gets or sets the voice-lifetime callback in source timing units.</summary>
    public Action<int>? Advance { get; set; }

    /// <inheritdoc/>
    public override void _Process(double delta) => Synchronize();

    /// <summary>Settles existing voices before a new occurrence so loading time cannot age an unstarted cue.</summary>
    public void Synchronize()
    {
        ulong now = Time.GetTicksUsec();
        float elapsed = (now - _lastMicroseconds) / 1000000f;
        _lastMicroseconds = now;
        Advance?.Invoke(_clock.Advance(elapsed));
    }
}
