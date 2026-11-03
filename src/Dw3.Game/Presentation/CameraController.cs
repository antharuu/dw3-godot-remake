using Dw3Production.Conversion;
using Godot;

namespace Dw3Production.Presentation;

/// <summary>Centers the exploration camera on the player while preserving map bounds and source-style panning.</summary>
public sealed class CameraController
{
    private const float ViewWidth = 1280f / 3f;
    private const float ViewHeight = 720f / 3f;
    private readonly Camera2D _camera;
    private Vector2 _pan;
    private bool _initialized;

    /// <summary>
    /// Initializes a new instance of the <see cref="CameraController"/> class.
    /// Sets the camera zoom to 3× and disables Godot position smoothing so the source
    /// exponential pan can be applied manually without the camera drifting off-map.
    /// </summary>
    /// <param name="camera">The Godot Camera2D node to drive; its zoom and smoothing are configured immediately.</param>
    public CameraController(Camera2D camera)
    {
        _camera = camera;
        _camera.Zoom = new Vector2(3f, 3f);

        // No engine smoothing: the source's exponential pan is applied manually in Apply,
        // so the camera stays clamped to the map instead of drifting off-map (black screen).
        _camera.PositionSmoothingEnabled = false;
    }

    /// <summary>Applies map limits and moves the camera to the player immediately or through quarter-distance panning.</summary>
    /// <param name="zone">Loaded field zone that supplies the map bounds in world pixels.</param>
    /// <param name="playerPosition">Player center in world pixels.</param>
    /// <param name="smooth"><see langword="true"/> to move one quarter of the remaining distance this update.</param>
    public void Apply(Zone zone, Vector2 playerPosition, bool smooth = false)
    {
        var target = PositionFor(zone.Width, zone.Height, playerPosition);
        _camera.LimitLeft = 0;
        _camera.LimitTop = 0;
        _camera.LimitRight = zone.Width;
        _camera.LimitBottom = zone.Height;
        if (!_initialized)
        {
            _pan = target;
            _initialized = true;
        }
        else if (smooth)
        {
            // FIELDSTG_updateCamera: pan toward the target by a quarter of the distance per
            // frame (exponential), so a climb hop pans instead of snapping, and the camera
            // never leaves the clamped map area.
            _pan += (target - _pan) / 4f;
        }
        else
        {
            _pan = target;
        }

        _camera.Position = _pan;
        _camera.MakeCurrent();
    }

    /// <summary>Calculates the camera center, clamping the player target to the visible map rectangle.</summary>
    /// <param name="mapWidth">Map width in world pixels.</param>
    /// <param name="mapHeight">Map height in world pixels.</param>
    /// <param name="playerPosition">Unclamped player center in world pixels.</param>
    /// <returns>A center that shows no area outside the map unless the map is smaller than the viewport.</returns>
    public static Vector2 PositionFor(int mapWidth, int mapHeight, Vector2 playerPosition)
    {
        var halfWidth = ViewWidth / 2f;
        var halfHeight = ViewHeight / 2f;
        var x = mapWidth <= ViewWidth ? mapWidth / 2f : Mathf.Clamp(playerPosition.X, halfWidth, mapWidth - halfWidth);
        var y = mapHeight <= ViewHeight ? mapHeight / 2f : Mathf.Clamp(playerPosition.Y, halfHeight, mapHeight - halfHeight);
        return new Vector2(x, y);
    }
}
