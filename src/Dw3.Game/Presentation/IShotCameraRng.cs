namespace Dw3Production.Presentation;

/// <summary>Supplies the random value used to choose the next battle shot list.</summary>
public interface IShotCameraRng
{
    /// <summary>Returns the next signed random value.</summary>
    int NextRoll();
}
