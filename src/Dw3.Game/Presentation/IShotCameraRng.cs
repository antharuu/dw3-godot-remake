namespace Dw3Production.Presentation;

/// <summary>Supplies the signed roll consumed when selecting the next battle camera shot list.</summary>
public interface IShotCameraRng
{
    /// <summary>Produces the next signed roll without imposing a range or distribution on implementations.</summary>
    /// <returns>The next signed value in the camera-selection sequence.</returns>
    int NextRoll();
}
