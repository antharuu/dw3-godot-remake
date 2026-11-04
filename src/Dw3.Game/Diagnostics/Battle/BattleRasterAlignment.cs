using Dw3Production.Presentation;
using Godot;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>Translates diagnostic actors so selected real source primitives overlap without changing a camera or mesh.</summary>
internal static class BattleRasterAlignment
{
    /// <summary>Places a selected source primitive at a viewport point and camera depth using translation alone.</summary>
    /// <param name="camera">Unmodified production source camera.</param>
    /// <param name="model">Diagnostic actor, never a persisted product placement.</param>
    /// <param name="bone">Bone carrying the observed ABR primitive.</param>
    /// <param name="pixel">Diagnostic overlap centre in viewport coordinates.</param>
    /// <param name="depth">Positive camera-space depth of the overlap.</param>
    public static void Align(Camera3D camera, BattleModel model, int bone, Vector2 pixel, float depth)
    {
        var polygon = model.EnumeratePolygons().First(item => item.Bone == bone && item.Abr >= 0);
        var centre = polygon.Positions.Select(position => model.WorldPosition(bone, position)).Aggregate(Vector3.Zero, (a, b) => a + b) / polygon.Size;
        model.Root.Position += camera.ProjectPosition(pixel, depth) - centre;
    }
}
