namespace Dw3Production.Presentation;

/// <summary>The shot camera's random source in normal play: the shared process random generator.</summary>
internal sealed class RuntimeShotRng : IShotCameraRng
{
    /// <summary>Draws the next non-negative random integer.</summary>
    /// <returns>A value in 0..int.MaxValue-1.</returns>
    public int Next() => Random.Shared.Next();
}
