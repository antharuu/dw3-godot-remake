namespace Dw3.Formats.Battle;

/// <summary>A decoded mesh: geometry streams, polygons and the file offsets they came from.</summary>
/// <param name="Bone">Bone that owns the mesh.</param>
/// <param name="Triangles">Triangle list; each quad (i0, i1, i2, i3) yields (i0, i1, i2) and (i2, i1, i3).</param>
/// <param name="EntryOffset">File offset of the mesh archive entry.</param>
/// <param name="CommandOffset">File offset of the command stream.</param>
/// <param name="BoundsOffset">File offset of the bounds entry.</param>
public sealed record BattleMesh(
    int Bone,
    IReadOnlyList<MeshVector> Vertices,
    IReadOnlyList<MeshVector> Normals,
    IReadOnlyList<int[]> Triangles,
    IReadOnlyList<MeshPolygon> Polygons,
    int EntryOffset,
    int CommandOffset,
    int BoundsOffset);
