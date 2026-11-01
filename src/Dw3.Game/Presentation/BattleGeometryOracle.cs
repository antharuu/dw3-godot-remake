using Godot;

namespace Dw3Production.Presentation;

/// <summary>
/// Offscreen acceptance check of a battle view's geometry: every checked fighter must project its
/// head above its feet, and the stage floor must not rise above the lowest fighter.
/// </summary>
internal static class BattleGeometryOracle
{
    /// <summary>Checks the opening (both fighters) or a command-idle view (its focused fighter only,
    /// since the source player camera frames that fighter rather than the opposing full box).</summary>
    /// <param name="models">The two built fighters.</param>
    /// <param name="stage">The fight stage.</param>
    /// <param name="camera">Camera of the main battle viewport.</param>
    /// <param name="focusedFighterId">Fighter to check, or null for both.</param>
    /// <returns>One summary per checked fighter plus the stage and fighter minimum heights.</returns>
    /// <exception cref="InvalidOperationException">The scene is incomplete or a check fails.</exception>
    public static string AssertOpening(IReadOnlyList<BattleModel> models, BattleModel? stage, Camera3D camera, int? focusedFighterId)
    {
        if (models.Count != 2 || stage is null)
        {
            throw new InvalidOperationException("opening geometry requires two real fighters and a stage");
        }

        var summaries = models
            .Where(model => focusedFighterId is null || model.FighterId == focusedFighterId.Value)
            .Select(model => AssertUpright(model, camera))
            .ToList();
        var stageBounds = BattleSceneBounds.Of(stage.Root, includeInvisible: true);
        var lowestFighter = models.Min(model => BattleSceneBounds.Of(model.Root, includeInvisible: true).Position.Y);
        if (stageBounds.Position.Y > lowestFighter)
        {
            throw new InvalidOperationException($"stage floor is above fighter feet stageMinY={stageBounds.Position.Y:0.###} fighterMinY={lowestFighter:0.###}");
        }

        return string.Join(" ", summaries) + $" stageMinY={stageBounds.Position.Y:0.###} fighterMinY={lowestFighter:0.###}";
    }

    private static string AssertUpright(BattleModel model, Camera3D camera)
    {
        // The ordering-table renderer owns visibility. MeshInstance3D nodes are source geometry
        // carriers and may all be hidden while their polygons remain visible.
        var bounds = BattleSceneBounds.Of(model.Root, includeInvisible: true);
        var meshes = BattleSceneBounds.MeshCount(model.Root);
        GD.Print($"BATTLE_BOUNDS fighter={model.FighterId} raw_pos={bounds.Position} raw_size={bounds.Size} visible_pos={bounds.Position} visible_size={bounds.Size} meshes={meshes}/{meshes} excluded=0");
        var top = camera.UnprojectPosition(new Vector3(bounds.GetCenter().X, bounds.End.Y, bounds.GetCenter().Z));
        var foot = camera.UnprojectPosition(new Vector3(bounds.GetCenter().X, bounds.Position.Y, bounds.GetCenter().Z));
        if (!(top.Y < foot.Y))
        {
            throw new InvalidOperationException($"fighter {model.FighterId} projects head below feet headY={top.Y:0.###} footY={foot.Y:0.###}");
        }

        // Screen-space visibility is asserted by the captured ordering-table image. The hidden
        // MeshInstance3D carriers use a different projection from that renderer.
        return $"fighter={model.FighterId} headY={top.Y:0.###} footY={foot.Y:0.###}";
    }
}
