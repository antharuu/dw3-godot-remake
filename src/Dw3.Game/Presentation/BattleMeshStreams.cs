using Godot;

namespace Dw3Production.Presentation;

/// <summary>
/// The decoded vertex and normal streams of one battle-model mesh, shared by all of its polygons.
/// </summary>
/// <param name="Bone">Source bone index the mesh is attached to.</param>
/// <param name="Vertices">Positions already converted to Godot axes and units.</param>
/// <param name="Normals">Unit normals already converted to Godot axes.</param>
internal sealed record BattleMeshStreams(int Bone, IReadOnlyList<Vector3> Vertices, IReadOnlyList<Vector3> Normals);
