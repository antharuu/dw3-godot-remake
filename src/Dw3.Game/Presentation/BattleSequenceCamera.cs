using Dw3Production.Simulation;

namespace Dw3Production.Presentation;

/// <summary>Resolves qualified sequence-camera commands through the same source view contract as the renderer.</summary>
public static class BattleSequenceCamera
{
    /// <summary>Builds a fresh composed camera frame from a decoded fighter view.</summary>
    /// <param name="cameras">Decoded fighter views.</param>
    /// <param name="index">Requested source view index.</param>
    /// <returns>Composed view with no extra root translation.</returns>
    public static SourceBattleCameraView FighterView(IReadOnlyList<BattleModel.BattleCameraView> cameras, int index)
    {
        if (index < 0 || index >= cameras.Count)
        {
            throw new FormatException($"Sequence camera view {index} is unavailable.");
        }

        var view = cameras[index];
        return new SourceBattleCameraView
        {
            Vpx = view.CamPos[0],
            Vpy = view.CamPos[1],
            Vpz = view.CamPos[2],
            Vrx = view.CamRef[0],
            Vry = view.CamRef[1],
            Vrz = view.CamRef[2],
            Projection = view.Proj,
        };
    }

    /// <summary>Resolves a preset or explicit fourteen-word source camera frame.</summary>
    /// <param name="request">Validated camera request.</param>
    /// <param name="views">Current fighter source views.</param>
    /// <returns>A fresh view without altering the menu camera.</returns>
    public static SourceBattleCameraView Resolve(CameraRequestEvent request, BattleShotCameraViews views)
    {
        var a = request.Arguments;
        if (a[1] == 0)
        {
            int id = a[2];
            return id switch
            {
                0 => views.GetEnemyView(),
                >= 1 and <= 4 => views.GetFighterView(0, id + 7),
                >= 5 and <= 7 => views.GetFighterView(0x10, id - 5),
                _ => throw new FormatException($"Unsupported sequence camera preset {id}."),
            };
        }

        // Authored coordinates are converted by the composed-view renderer exactly once.
        return new SourceBattleCameraView
        {
            Vpx = a[2],
            Vpy = a[3],
            Vpz = a[4],
            Vrx = a[5],
            Vry = a[6],
            Vrz = a[7],
            Tx = a[8],
            Ty = a[9],
            Tz = a[10],
            RotationX = a[11],
            RotationY = a[12],
            RotationZ = a[13],
            Rz = a[14],
            Projection = a[15],
        };
    }

    /// <summary>Applies the composed sequence view to the renderer's physical interpolation state.</summary>
    /// <param name="request">Validated request.</param>
    /// <param name="views">Source camera presets.</param>
    /// <param name="camera">Physical camera interpolation state shared with the renderer.</param>
    internal static void Apply(CameraRequestEvent request, BattleShotCameraViews views, BattleCameraFade camera)
    {
        var view = Resolve(request, views);
        camera.BeginSource(view, request.Arguments[0]);
    }
}
