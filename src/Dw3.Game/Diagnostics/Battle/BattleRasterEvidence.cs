using System.Text.Json;
using Dw3Production.Presentation;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>Exports collected GPU inputs without substituting a software compositor for the executor.</summary>
internal static class BattleRasterEvidence
{
    /// <summary>Records diagnostic actor translations and the unchanged source camera parameters.</summary>
    /// <param name="renderer">Production renderer being captured.</param>
    /// <param name="root">Private evidence directory.</param>
    public static void WriteScene(BattleRenderer renderer, string root)
    {
        var partner = renderer.PartnerForProbe;
        var state = new
        {
            poseFrame = 10,
            shotSelectionRoll = 0,
            stage = 18,
            placement = "diagnostic translations only, no product placement/camera change",
            mainCamera = Camera(renderer.CameraForProbe),
            rawCamera = Camera(partner.CameraForProbe),
            mainActors = new[] { Actor(renderer.ModelForProbe(374)), Actor(renderer.ModelForProbe(51)) },
            rawActors = new[] { Actor(partner.RequiredModelForProbe), Actor(partner.OpponentForProbe) },
        };
        File.WriteAllText(Path.Combine(root, "scene.json"), JsonSerializer.Serialize(state));
    }

    /// <summary>Writes source identities, projected corners, colours and the actual mutable GPU pages.</summary>
    /// <param name="control">Recorded production source stream in draw order.</param>
    /// <param name="root">Private evidence directory, never a repository asset directory.</param>
    /// <param name="name">View name used to distinguish the independent streams.</param>
    public static void Write(BattleRasterControl control, string root, string name)
    {
        var primitives = control.Ordered.Select((item, index) => new
        {
            index,
            fighter = item.Fighter,
            key = item.Key,
            bone = item.Polygon.Bone,
            vertexStart = item.Polygon.VertexStart,
            size = item.Polygon.Size,
            textureKey = item.Polygon.TextureKey,
            runtimeAbr = item.Polygon.Abr,
            storedAbr = item.Polygon.Abr + 1,
            screen = item.Screen.Select(value => new[] { value.X, value.Y }),
            uv = item.Polygon.Uvs.Select(value => new[] { value.X * item.Texture.GetWidth(), value.Y * item.Texture.GetHeight() }),
            colors = item.Polygon.Colors.Select(value => new[] { value.R, value.G, value.B, value.A }),
        });
        File.WriteAllText(Path.Combine(root, $"{name}-stream.json"), JsonSerializer.Serialize(primitives));
        foreach (var page in control.Ordered.DistinctBy(item => (item.Fighter, item.Polygon.TextureKey)))
        {
            using var image = page.Texture.GetImage();
            image.SavePng(Path.Combine(root, $"{name}-f{page.Fighter}-t{page.Polygon.TextureKey}.png"));
        }
    }

    /// <summary>Converts a source camera to explicit numeric evidence without serializing engine objects.</summary>
    /// <param name="camera">Unchanged production camera.</param>
    /// <returns>Position, Euler rotation and vertical projection angle.</returns>
    private static object Camera(Godot.Camera3D camera) => new
    {
        position = new[] { camera.GlobalPosition.X, camera.GlobalPosition.Y, camera.GlobalPosition.Z },
        rotation = new[] { camera.GlobalRotation.X, camera.GlobalRotation.Y, camera.GlobalRotation.Z },
        fov = camera.Fov,
    };

    /// <summary>Converts a diagnostic actor to explicit transformation evidence.</summary>
    /// <param name="model">Translated diagnostic actor.</param>
    /// <returns>Source identity, position, rotation and scale.</returns>
    private static object Actor(BattleModel model) => new
    {
        fighter = model.FighterId,
        position = new[] { model.Root.GlobalPosition.X, model.Root.GlobalPosition.Y, model.Root.GlobalPosition.Z },
        rotation = new[] { model.Root.GlobalRotation.X, model.Root.GlobalRotation.Y, model.Root.GlobalRotation.Z },
        scale = new[] { model.Root.Scale.X, model.Root.Scale.Y, model.Root.Scale.Z },
    };
}
