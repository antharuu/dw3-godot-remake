using Godot;

namespace Dw3Production.Presentation;

/// <summary>
/// Projects a source model polygon's world corners to the battle viewport under the hardware rules of
/// <see cref="BattleGteProjection"/>: corners keep the viewport camera's exact projection while the GTE division holds,
/// take the saturated GTE screen point once any corner overflows it, and the polygon is dropped when the GPU would
/// refuse its span.
/// </summary>
internal static class BattleGteScreen
{
    /// <summary>Bound of a converted view coordinate, far beyond every register the GTE saturates to.</summary>
    private const float SourceLimit = 1_000_000_000f;

    /// <summary>Projects one polygon.</summary>
    /// <param name="camera">Viewport camera of the battle scene.</param>
    /// <param name="world">World positions of the polygon corners.</param>
    /// <param name="screen">Receives the viewport corners, empty when the polygon is dropped.</param>
    /// <returns>Whether the GPU draws the polygon.</returns>
    public static bool TryProject(Camera3D camera, IReadOnlyList<Vector3> world, out Vector2[] screen)
    {
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(world);
        Vector2 size = camera.GetViewport().GetVisibleRect().Size;
        float scale = size.Y / (BattleSceneMath.Ps1ScreenHalfHeight * 2f);
        int projection = (int)MathF.Round(BattleSceneMath.Ps1ScreenHalfHeight / MathF.Tan(Mathf.DegToRad(camera.Fov) / 2f));
        Transform3D inverse = camera.GlobalTransform.AffineInverse();
        var source = new (int X, int Y)[world.Count];
        bool overflow = false;
        for (int i = 0; i < world.Count; i++)
        {
            Vector3 view = inverse * world[i];
            int depth = ToSource(-view.Z);
            overflow |= BattleGteProjection.Overflows(depth, projection);
            source[i] = BattleGteProjection.Project(ToSource(view.X), ToSource(-view.Y), depth, projection);
        }

        if (!BattleGteProjection.GpuDraws(source))
        {
            screen = [];
            return false;
        }

        Vector2 center = size / 2f;
        screen = overflow
            ? [.. source.Select(point => center + (new Vector2(point.X - BattleGteProjection.OffsetX, point.Y - BattleGteProjection.OffsetY) * scale))]
            : [.. world.Select(camera.UnprojectPosition)];
        return true;
    }

    /// <summary>Converts a view-space distance to rounded source units.</summary>
    /// <param name="value">Distance in Godot units.</param>
    /// <returns>Distance in source units.</returns>
    private static int ToSource(float value) => (int)Math.Clamp(MathF.Round(value * BattleSceneMath.Ps1UnitsPerGodotUnit), -SourceLimit, SourceLimit);
}
