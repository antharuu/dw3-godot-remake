using Godot;
using Dw3Production.Conversion;

namespace Dw3Production.Presentation;

public sealed class CameraController
{
    private const float ViewWidth = 1280f / 3f;
    private const float ViewHeight = 720f / 3f;
    private readonly Camera2D _camera;
    private Vector2 _pan;
    private bool _initialized;

    public CameraController(Camera2D camera)
    {
        _camera = camera;
        _camera.Zoom = new Vector2(3f, 3f);
        // No engine smoothing: the source's exponential pan is applied manually in Apply,
        // so the camera stays clamped to the map instead of drifting off-map (black screen).
        _camera.PositionSmoothingEnabled = false;
    }

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

    public static Vector2 PositionFor(int mapWidth, int mapHeight, Vector2 playerPosition)
    {
        var halfWidth = ViewWidth / 2f;
        var halfHeight = ViewHeight / 2f;
        var x = mapWidth <= ViewWidth ? mapWidth / 2f : Mathf.Clamp(playerPosition.X, halfWidth, mapWidth - halfWidth);
        var y = mapHeight <= ViewHeight ? mapHeight / 2f : Mathf.Clamp(playerPosition.Y, halfHeight, mapHeight - halfHeight);
        return new Vector2(x, y);
    }
}
