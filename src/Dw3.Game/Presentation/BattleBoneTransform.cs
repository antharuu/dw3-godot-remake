using Godot;

namespace Dw3Production.Presentation;

/// <summary>Builds source-style local bone transforms for retained battle skeleton matrices.</summary>
internal static class BattleBoneTransform
{
    /// <summary>Composes rotation and nonuniform scale in the bone's local axes.</summary>
    /// <param name="basis">Unscaled local rotation basis.</param>
    /// <param name="position">Local translation in presentation coordinates.</param>
    /// <param name="scale">Local scale applied after the rotation.</param>
    /// <returns>The local rotation-times-scale transform.</returns>
    public static Transform3D Compose(Basis basis, Vector3 position, Vector3 scale) =>
        new(basis.ScaledLocal(scale), position);
}
