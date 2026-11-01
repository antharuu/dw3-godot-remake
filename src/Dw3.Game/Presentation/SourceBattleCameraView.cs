namespace Dw3Production.Presentation;

/// <summary>Mutable camera frame used by the battle renderer.</summary>
public sealed class SourceBattleCameraView
{
    /// <summary>Gets or sets the viewpoint X coordinate.</summary>
    public int Vpx { get; set; }

    /// <summary>Gets or sets the viewpoint Y coordinate.</summary>
    public int Vpy { get; set; }

    /// <summary>Gets or sets the viewpoint Z coordinate.</summary>
    public int Vpz { get; set; }

    /// <summary>Gets or sets the look-at X coordinate.</summary>
    public int Vrx { get; set; }

    /// <summary>Gets or sets the look-at Y coordinate.</summary>
    public int Vry { get; set; }

    /// <summary>Gets or sets the look-at Z coordinate.</summary>
    public int Vrz { get; set; }

    /// <summary>Gets or sets the camera-frame X translation.</summary>
    public int Tx { get; set; }

    /// <summary>Gets or sets the camera-frame Y translation.</summary>
    public int Ty { get; set; }

    /// <summary>Gets or sets the camera-frame Z translation.</summary>
    public int Tz { get; set; }

    /// <summary>Gets or sets the camera-frame X rotation, with 4096 units per turn.</summary>
    public int RotationX { get; set; }

    /// <summary>Gets or sets the camera-frame Y rotation, with 4096 units per turn.</summary>
    public int RotationY { get; set; }

    /// <summary>Gets or sets the camera-frame Z rotation, with 4096 units per turn.</summary>
    public int RotationZ { get; set; }

    /// <summary>Gets or sets the view roll.</summary>
    public int Rz { get; set; }

    /// <summary>Gets or sets the projection-plane distance.</summary>
    public int Projection { get; set; }
}
