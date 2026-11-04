namespace Dw3Production.Presentation;

/// <summary>Restores the command camera random source after a deterministic diagnostic replay.</summary>
internal sealed class ShotCameraRngScope : IDisposable
{
    private readonly BattleCommandCamera _camera;
    private readonly IShotCameraRng _previous;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="ShotCameraRngScope"/> class.</summary>
    /// <param name="camera">Command camera whose source must be restored.</param>
    /// <param name="previous">Runtime source that was active before the probe.</param>
    public ShotCameraRngScope(BattleCommandCamera camera, IShotCameraRng previous)
    {
        _camera = camera;
        _previous = previous;
    }

    /// <summary>Restores the normal camera random source exactly once.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _camera.ReplaceRngForProbe(_previous);
        _disposed = true;
    }
}
