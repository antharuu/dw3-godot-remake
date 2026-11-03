namespace Dw3Production.Presentation;

/// <summary>
/// Maps a PS1 ordering-table entry to Godot CanvasItem order. The PS1 renderer
/// submits the ordering table from its highest entry to its lowest, so smaller
/// source depths are drawn later and appear nearer. Godot draws larger ZIndex
/// values later, hence the sign inversion.
/// </summary>
public static class SourceDepth
{
    /// <summary>Inverts an ordering-table depth so the near primitive receives the larger canvas Z index.</summary>
    /// <param name="orderingTableDepth">Depth bucket assigned by the source painter-order pass.</param>
    /// <returns>The Godot canvas Z index that preserves source draw order.</returns>
    public static int ToGodotZIndex(int orderingTableDepth) => -orderingTableDepth;
}
