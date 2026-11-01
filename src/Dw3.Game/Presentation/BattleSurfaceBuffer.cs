using Godot;

namespace Dw3Production.Presentation;

/// <summary>
/// Accumulates the emitted corners of one battle-model surface before it is committed as a Godot
/// mesh. Corners are kept in emission order, so committing replays exactly the colour, UV and
/// position sequence the surface tool would have received while reading the polygons.
/// </summary>
internal sealed class BattleSurfaceBuffer
{
    /// <summary>Gets the emitted positions in bone-local Godot units.</summary>
    public List<Vector3> Positions { get; } = [];

    /// <summary>Gets the emitted texture coordinates (source texel / 256).</summary>
    public List<Vector2> Uvs { get; } = [];

    /// <summary>Gets the lighting inputs, whose <see cref="BattleLightingVertex.Flat"/> is the baked colour.</summary>
    public List<BattleLightingVertex> Lighting { get; } = [];

    /// <summary>Gets one entry per source primitive: its command index, first emitted vertex and corner count.</summary>
    public List<(int Command, int VertexStart, int Size)> SourceStates { get; } = [];

    /// <summary>Appends one emitted corner.</summary>
    /// <param name="position">Corner position in bone-local Godot units.</param>
    /// <param name="uv">Corner texture coordinate in the 0..1 page range.</param>
    /// <param name="lighting">Corner lighting inputs and baked colour.</param>
    public void Add(Vector3 position, Vector2 uv, BattleLightingVertex lighting)
    {
        Positions.Add(position);
        Uvs.Add(uv);
        Lighting.Add(lighting);
    }

    /// <summary>Builds the triangle-list mesh with the baked colours.</summary>
    /// <returns>The committed mesh.</returns>
    public ArrayMesh Commit()
    {
        var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        for (var index = 0; index < Positions.Count; index++)
        {
            tool.SetColor(Lighting[index].Flat);
            tool.SetUV(Uvs[index]);
            tool.AddVertex(Positions[index]);
        }

        return tool.Commit();
    }
}
