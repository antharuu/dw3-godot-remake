using Dw3Production.Presentation;
using Dw3Production.Simulation;
using Godot;
using NumericsVector2 = System.Numerics.Vector2;

namespace Dw3Production.Diagnostics.Field;

/// <summary>
/// Maps field world coordinates to 1280x720 viewport pixels for the camera that follows the player.
/// One world unit is drawn as a 3x3 pixel block and the camera centre sits at the viewport centre.
/// </summary>
internal readonly struct FieldScreenProjection
{
    /// <summary>Viewport pixels per world unit on each axis.</summary>
    private const float PixelsPerWorldUnit = 3f;

    /// <summary>Camera centre in world units.</summary>
    private readonly Vector2 _camera;

    /// <summary>Viewport pixel that the camera centre maps to: (640, 360) for block corners, (641, 361) for block centres.</summary>
    private readonly Vector2 _origin;

    /// <summary>Initializes a new instance of the <see cref="FieldScreenProjection"/> struct.</summary>
    /// <param name="camera">Camera centre in world units.</param>
    /// <param name="origin">Viewport pixel of the camera centre.</param>
    private FieldScreenProjection(Vector2 camera, Vector2 origin)
    {
        _camera = camera;
        _origin = origin;
    }

    /// <summary>Gets the camera centre in world units.</summary>
    public Vector2 Camera => _camera;

    /// <summary>Creates the projection that returns the top-left pixel of each world unit block.</summary>
    /// <param name="zone">Zone whose size clamps the camera.</param>
    /// <param name="player">Player position the camera follows.</param>
    /// <returns>The projection with its origin at (640, 360).</returns>
    public static FieldScreenProjection BlockCorners(FieldZone zone, NumericsVector2 player) =>
        new(CameraFor(zone, player), new Vector2(640f, 360f));

    /// <summary>Creates the projection that returns the centre pixel of each world unit block.</summary>
    /// <param name="zone">Zone whose size clamps the camera.</param>
    /// <param name="player">Player position the camera follows.</param>
    /// <returns>The projection with its origin at (641, 361).</returns>
    public static FieldScreenProjection BlockCentres(FieldZone zone, NumericsVector2 player) =>
        new(CameraFor(zone, player), new Vector2(641f, 361f));

    /// <summary>Returns the viewport column of a world X coordinate.</summary>
    /// <param name="worldX">World X in map units.</param>
    /// <returns>Rounded viewport column; may fall outside the viewport.</returns>
    public int X(float worldX) => Mathf.RoundToInt(((worldX - _camera.X) * PixelsPerWorldUnit) + _origin.X);

    /// <summary>Returns the viewport row of a world Y coordinate.</summary>
    /// <param name="worldY">World Y in map units.</param>
    /// <returns>Rounded viewport row; may fall outside the viewport.</returns>
    public int Y(float worldY) => Mathf.RoundToInt(((worldY - _camera.Y) * PixelsPerWorldUnit) + _origin.Y);

    /// <summary>Returns the camera centre that the field camera uses for a player position.</summary>
    /// <param name="zone">Zone whose size clamps the camera.</param>
    /// <param name="player">Player position.</param>
    /// <returns>Camera centre in world units.</returns>
    private static Vector2 CameraFor(FieldZone zone, NumericsVector2 player) =>
        CameraController.PositionFor(zone.Zone.Width, zone.Zone.Height, new Vector2(player.X, player.Y));
}
