using Godot;

namespace Dw3Production.Presentation;

/// <summary>Stores the three fixed-point transform channels of one bone pose.</summary>
/// <param name="Position">Source position channel.</param>
/// <param name="Rotation">Source rotation channel.</param>
/// <param name="Scale">Source scale channel.</param>
internal readonly record struct BattleBlendPose(Vector3 Position, Vector3 Rotation, Vector3 Scale)
{
    /// <summary>Gets the default source-space pose.</summary>
    public static BattleBlendPose Identity { get; } = new(Vector3.Zero, Vector3.Zero, new Vector3(4096, 4096, 4096));
}
