using Godot;

namespace Dw3Production.Presentation;

/// <summary>
/// The HUD animation clock in milliseconds. Sprite blinks and cursor cycles advance every 16 ms
/// source frame; a probe can pin the clock to a deterministic value.
/// </summary>
internal sealed class BattleHudClock
{
    /// <summary>Gets or sets the pinned time, or null to follow the engine tick counter.</summary>
    public ulong? ProbeMilliseconds { get; set; }

    /// <summary>Gets the current animation time, read again on every access when not pinned.</summary>
    public ulong Milliseconds => ProbeMilliseconds ?? Time.GetTicksMsec();
}
