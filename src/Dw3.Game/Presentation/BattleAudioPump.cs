using Godot;

namespace Dw3Production.Presentation;

/// <summary>Advances physical note deadlines even when an action has already ended.</summary>
internal sealed partial class BattleAudioPump : Node
{
    private readonly BattleSourceClock _clock = new();

    /// <summary>Gets or sets the voice-lifetime callback in source timing units.</summary>
    public Action<int>? Advance { get; set; }

    /// <inheritdoc/>
    public override void _Process(double delta) => Advance?.Invoke(_clock.Advance((float)delta));
}
