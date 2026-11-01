namespace Dw3Production.Presentation;

/// <summary>
/// Maps a PS1 ordering-table entry to Godot CanvasItem order. The PS1 renderer
/// submits the ordering table from its highest entry to its lowest, so smaller
/// source depths are drawn later and appear nearer. Godot draws larger ZIndex
/// values later, hence the sign inversion.
/// </summary>
public static class SourceDepth
{
    public static int ToGodotZIndex(int orderingTableDepth) => -orderingTableDepth;
}
