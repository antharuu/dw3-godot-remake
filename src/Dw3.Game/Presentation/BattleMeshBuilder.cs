using System.Text.Json;
using Godot;

namespace Dw3Production.Presentation;

/// <summary>
/// Builds the Godot surfaces of a battle model from the JSON <c>meshes</c> array. Each mesh is
/// attached to one bone; its polygons are grouped by (texture key, ABR) into one surface each,
/// triangles kept as three corners and quads split into the corner order 0,1,2 / 2,1,3.
/// </summary>
/// <remarks>
/// A polygon whose source command disabled UV loading still renders through the residual textured
/// state, so the extractor keeps the preceding command's UV/tpage on it and only its colour
/// differs. Such polygons are counted in <see cref="UntexturedPolygonCount"/>.
/// </remarks>
internal sealed class BattleMeshBuilder
{
    private static readonly int[] TriangleCorners = [0, 1, 2];
    private static readonly int[] QuadCorners = [0, 1, 2, 2, 1, 3];

    /// <summary>The fallback light direction used before the stage light set is bound (Godot axes).</summary>
    private static readonly Vector3 FallbackLightDirection = new(-0.35f, 0.8f, 0.45f);

    private readonly BattleSkeleton _skeleton;
    private readonly IReadOnlyDictionary<int, BattleFaceTexture> _textures;

    /// <summary>
    /// Initializes a new instance of the <see cref="BattleMeshBuilder"/> class.
    /// </summary>
    /// <param name="skeleton">Skeleton whose bone nodes receive the mesh instances.</param>
    /// <param name="textures">Decoded texture pages by key.</param>
    public BattleMeshBuilder(BattleSkeleton skeleton, IReadOnlyDictionary<int, BattleFaceTexture> textures)
    {
        _skeleton = skeleton;
        _textures = textures;
    }

    /// <summary>Gets the material of every built surface, in build order.</summary>
    public List<ShaderMaterial> Materials { get; } = [];

    /// <summary>Gets every built surface, in build order.</summary>
    public List<BattleLitSurface> Surfaces { get; } = [];

    /// <summary>Gets where each source primitive was emitted, in build order.</summary>
    public List<BattleSourcePolygonState> SourcePolygons { get; } = [];

    /// <summary>Gets the number of polygons drawn through residual textured state.</summary>
    public int UntexturedPolygonCount { get; private set; }

    /// <summary>Gets the number of surfaces built.</summary>
    public int MeshCount { get; private set; }

    /// <summary>Builds every mesh of the array.</summary>
    /// <param name="meshes">The <c>meshes</c> array.</param>
    /// <exception cref="InvalidOperationException">A polygon lacks UV state, is neither a triangle nor
    /// a quad, or names a missing texture key.</exception>
    public void Build(JsonElement meshes)
    {
        foreach (var mesh in meshes.EnumerateArray())
        {
            var streams = new BattleMeshStreams(mesh.GetProperty("bone").GetInt32(), ReadVertices(mesh), ReadNormals(mesh));
            foreach (var (key, polygons) in GroupPolygons(mesh))
            {
                BuildSurface(streams, key, polygons);
            }
        }
    }

    /// <summary>Reads and converts the vertex stream of one mesh.</summary>
    /// <param name="mesh">Mesh JSON object containing the vertex array.</param>
    /// <returns>Vertices in presentation coordinates.</returns>
    private static List<Vector3> ReadVertices(JsonElement mesh) =>
        [.. mesh.GetProperty("vertices").EnumerateArray().Select(vertex =>
            BattleModel.Ps1Position(new Vector3(vertex[0].GetSingle(), vertex[1].GetSingle(), vertex[2].GetSingle())))];

    /// <summary>Reads and converts the normal stream of one mesh.</summary>
    /// <param name="mesh">Mesh JSON object containing the normal array.</param>
    /// <returns>Normalized normals in presentation coordinates.</returns>
    private static List<Vector3> ReadNormals(JsonElement mesh) =>
        [.. mesh.GetProperty("normals").EnumerateArray().Select(normal =>
            new Vector3(normal[0].GetSingle(), -normal[1].GetSingle(), -normal[2].GetSingle()).Normalized())];

    /// <summary>Picks the normal of a corner: its own for a Gouraud polygon, the first one otherwise.</summary>
    /// <param name="polygon">A lit polygon record.</param>
    /// <param name="corner">Source corner index 0..3.</param>
    /// <param name="normals">The mesh normal stream.</param>
    /// <returns>The unit normal in Godot axes.</returns>
    private static Vector3 CornerNormal(JsonElement polygon, int corner, IReadOnlyList<Vector3> normals)
    {
        var normalIndices = polygon.GetProperty("normal_indices");
        var sourceCorner = polygon.GetProperty("gouraud").GetBoolean() ? corner : 0;
        return normals[normalIndices[sourceCorner].GetInt32()];
    }

    /// <summary>
    /// The baked corner colour as source RGB/255: the flat colour of an unlit polygon, or for a lit
    /// one a neutral ambient 0x40 plus one directional term. The stage light set replaces the lit
    /// value once bound.
    /// </summary>
    /// <param name="polygon">A polygon record.</param>
    /// <param name="corner">Source corner index 0..3.</param>
    /// <param name="normals">The mesh normal stream.</param>
    /// <returns>The colour as source RGB/255.</returns>
    private static Color PolygonColor(JsonElement polygon, int corner, IReadOnlyList<Vector3> normals)
    {
        if (!polygon.GetProperty("lit").GetBoolean())
        {
            var flat = polygon.GetProperty("flat_color");
            return new Color(flat[0].GetSingle() / 255f, flat[1].GetSingle() / 255f, flat[2].GetSingle() / 255f);
        }

        var normal = CornerNormal(polygon, corner, normals);
        var intensity = Mathf.Clamp(0.5f + (Mathf.Max(0f, normal.Dot(FallbackLightDirection)) * 0.9f), 0f, 2f);
        return new Color(intensity * 128f / 255f, intensity * 128f / 255f, intensity * 128f / 255f);
    }

    /// <summary>Appends the triangles representing one polygon to a surface buffer.</summary>
    /// <param name="buffer">Surface buffer receiving vertices and state.</param>
    /// <param name="streams">Vertex and normal streams for the mesh.</param>
    /// <param name="polygon">Polygon record to append.</param>
    /// <param name="sourceCommand">Ordinal of the polygon within the mesh.</param>
    /// <exception cref="InvalidOperationException">The polygon is neither a triangle nor a quad.</exception>
    private static void AppendPolygon(BattleSurfaceBuffer buffer, BattleMeshStreams streams, JsonElement polygon, int sourceCommand)
    {
        var indices = polygon.GetProperty("indices");
        var uv = polygon.GetProperty("uv");
        var count = indices.GetArrayLength();
        if (count != 3 && count != 4)
        {
            throw new InvalidOperationException("battle model polygon is not triangle or quad");
        }

        var vertexStart = buffer.Positions.Count;
        foreach (var i in count == 3 ? TriangleCorners : QuadCorners)
        {
            var color = PolygonColor(polygon, i, streams.Normals);
            var vertex = streams.Vertices[indices[i].GetInt32()];
            var texUv = new Vector2(uv[i][0].GetSingle() / 256f, uv[i][1].GetSingle() / 256f);
            var isLit = polygon.GetProperty("lit").GetBoolean();
            var normal = isLit ? CornerNormal(polygon, i, streams.Normals) : Vector3.Up;
            buffer.Add(vertex, texUv, new BattleLightingVertex(normal, isLit, color));
        }

        buffer.SourceStates.Add((sourceCommand, vertexStart, count));
    }

    /// <summary>Groups mesh polygons by texture and blend mode for surface construction.</summary>
    /// <param name="mesh">Mesh JSON object containing polygons.</param>
    /// <returns>Polygons grouped by texture key and blend mode.</returns>
    /// <exception cref="InvalidOperationException">A polygon has no retained UV state.</exception>
    private Dictionary<(int Texture, int Abr), List<(JsonElement Polygon, int Command)>> GroupPolygons(JsonElement mesh)
    {
        var groups = new Dictionary<(int Texture, int Abr), List<(JsonElement Polygon, int Command)>>();
        var command = 0;
        foreach (var polygon in mesh.GetProperty("polygons").EnumerateArray())
        {
            if (polygon.GetProperty("uv").ValueKind == JsonValueKind.Null)
            {
                throw new InvalidOperationException("battle model polygon lacks residual UV state");
            }

            if (!polygon.GetProperty("textured").GetBoolean())
            {
                UntexturedPolygonCount++;
            }

            var key = (polygon.GetProperty("texture_key").GetInt32(), polygon.GetProperty("abr").GetInt32());
            if (!groups.TryGetValue(key, out var polygons))
            {
                groups[key] = polygons = [];
            }

            polygons.Add((polygon, command++));
        }

        return groups;
    }

    /// <summary>Builds and attaches one material surface from a polygon group.</summary>
    /// <param name="streams">Vertex and normal streams for the owning mesh.</param>
    /// <param name="key">Texture key and blend mode of the group.</param>
    /// <param name="polygons">Polygons to emit into the surface.</param>
    /// <exception cref="InvalidOperationException">The group references an unavailable texture.</exception>
    private void BuildSurface(BattleMeshStreams streams, (int Texture, int Abr) key, List<(JsonElement Polygon, int Command)> polygons)
    {
        if (!_textures.TryGetValue(key.Texture, out var texture))
        {
            throw new InvalidOperationException($"battle model texture key {key.Texture} is missing");
        }

        var buffer = new BattleSurfaceBuffer();
        foreach (var (polygon, sourceCommand) in polygons)
        {
            AppendPolygon(buffer, streams, polygon, sourceCommand);
        }

        var material = BattlePs1Material.Create(texture.Texture, key.Abr == 0, cullBack: true);
        Materials.Add(material);
        var instance = new MeshInstance3D { Mesh = buffer.Commit(), MaterialOverride = material };
        _skeleton.RenderRoot.AddChild(instance);
        var surface = new BattleLitSurface(instance, streams.Bone, [.. buffer.Positions], [.. buffer.Uvs], [.. buffer.Lighting], material, key.Abr);
        Surfaces.Add(surface);
        foreach (var state in buffer.SourceStates)
        {
            SourcePolygons.Add(new BattleSourcePolygonState(surface, streams.Bone, state.Command, state.VertexStart, state.Size, key.Texture, key.Abr));
        }

        MeshCount++;
    }
}
