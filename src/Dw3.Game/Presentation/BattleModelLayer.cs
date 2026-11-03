using Godot;

namespace Dw3Production.Presentation;

/// <summary>
/// The PS1 BATTLE_LAYER_MODELS equivalent. the decoded mesh contract adds both fighters' primitives to one
/// otShift=12 ordering table, then draws that layer over the z-buffered stage. This node rebuilds
/// the one mesh each Present because the PS1 key is based on the posed, projected vertices.
/// </summary>
public sealed class BattleModelLayer : IDisposable
{
    private readonly MeshInstance3D _instance;
    private readonly ShaderMaterial _material;
    private readonly Dictionary<(BattleModel Model, int TextureKey), int> _pageLayers = [];
    private Texture2DArray? _pages;
    private bool _dirtyPages = true;

    /// <summary>Initializes a new instance of the <see cref="BattleModelLayer"/> class, creating the mesh instance and shader material and attaching them to <paramref name="parent"/>.</summary>
    /// <param name="parent">3-D parent node that receives the mesh instance.</param>
    /// <param name="name">Name applied to the mesh instance node.</param>
    public BattleModelLayer(Node3D parent, string name)
    {
        _instance = new MeshInstance3D { Name = name, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        _material = new ShaderMaterial { Shader = new Shader { Code = ShaderCode } };
        _instance.MaterialOverride = _material;
        _material.RenderPriority = 10;
        parent.AddChild(_instance);
    }

    /// <summary>Gets the total number of polygons submitted to the last Rebuild call.</summary>
    public int PolygonCount { get; private set; }

    /// <summary>Gets the number of polygons rejected by the back-face test in the last Rebuild call.</summary>
    public int CulledCount { get; private set; }

    /// <summary>Gets the number of semi-transparent polygons skipped because they cannot be depth-sorted by this layer.</summary>
    public int AbrUnsortedCount { get; private set; }

    /// <summary>Releases Godot resources owned by this layer.</summary>
    public void Dispose()
    {
        _pages?.Dispose();
        _material.Dispose();
        _instance.Dispose();
    }

    /// <summary>Resets the mesh, page table, and counters so the next Rebuild starts from a clean state.</summary>
    public void Clear()
    {
        _pageLayers.Clear();
        _pages = null;
        _instance.Mesh = null;
        _dirtyPages = true;
        PolygonCount = CulledCount = AbrUnsortedCount = 0;
    }

    /// <summary>Rebuilds the ordered model mesh for the supplied camera and fighters.</summary>
    /// <param name="camera">Camera used to project and depth-sort polygons.</param>
    /// <param name="models">Visible fighter models contributing polygons.</param>
    public void Rebuild(Camera3D camera, IReadOnlyList<BattleModel> models)
    {
        EnsurePages(models);
        var pending = CollectPending(camera, models);
        PolygonCount = pending.Count;
        EmitMesh(pending);
        foreach (var model in models)
        {
            model.SetPainterLayerActive(true);
        }
    }

    /// <summary>Collects visible polygons eligible for ordered rendering.</summary>
    /// <param name="camera">Camera used for projection.</param>
    /// <param name="models">Fighter models to inspect.</param>
    /// <returns>Pending polygons ready for mesh emission.</returns>
    private List<PendingPolygon> CollectPending(Camera3D camera, IReadOnlyList<BattleModel> models)
    {
        var pending = new List<PendingPolygon>();
        PolygonCount = CulledCount = AbrUnsortedCount = 0;
        foreach (var model in models)
        {
            if (!model.Visible)
            {
                continue;
            }

            CollectModelPolygons(camera, model, pending);
        }

        return pending;
    }

    /// <summary>Collects drawable polygons from one model.</summary>
    /// <param name="camera">Camera used for projection.</param>
    /// <param name="model">Model supplying polygons.</param>
    /// <param name="pending">Destination collection for accepted polygons.</param>
    private void CollectModelPolygons(Camera3D camera, BattleModel model, List<PendingPolygon> pending)
    {
        foreach (var polygon in model.EnumeratePolygons())
        {
            if (!model.IsBoneVisible(polygon.Bone))
            {
                continue;
            }

            // -1 is opaque. 0 is the source's first semi-transparent mode, stored in
            // MeshDrawState.abr as 1. Modes 1..3 retain their existing surfaces.
            if (polygon.Abr > 0)
            {
                AbrUnsortedCount++;
                continue;
            }

            var world = polygon.Positions.Select(position => model.WorldPosition(polygon.Bone, position)).ToArray();
            var screen = world.Select(camera.UnprojectPosition)
                .Select(value => new System.Numerics.Vector2(value.X, value.Y)).ToArray();

            // Godot's camera projects its visible front faces with the inverse winding of
            // the PS1 GTE stream after Ps1Position's Y/Z conversion. Feed the source order
            // through that conversion before applying the exact positive-OPZ predicate.
            if (!RenderOrderingTable.FacesCamera(screen[0], screen[2], screen[1], polygon.Size == 4 ? screen[3] : null))
            {
                CulledCount++;
                continue;
            }

            AddPendingPolygon(camera, model, polygon, world, pending);
        }
    }

    /// <summary>Calculates an ordering key and queues one polygon for mesh emission.</summary>
    /// <param name="camera">Camera used for depth calculation.</param>
    /// <param name="model">Model owning the polygon.</param>
    /// <param name="polygon">Polygon source data.</param>
    /// <param name="world">World-space polygon vertices.</param>
    /// <param name="pending">Destination collection for the polygon.</param>
    private void AddPendingPolygon(Camera3D camera, BattleModel model, BattleModel.SourcePolygon polygon, Godot.Vector3[] world, List<PendingPolygon> pending)
    {
        var cameraInverse = camera.GlobalTransform.AffineInverse();
        var depths = world.Select(position => (int)MathF.Round(-(cameraInverse * position).Z * BattleSceneMath.Ps1UnitsPerGodotUnit)).ToArray();
        var key = polygon.Size == 3
            ? RenderOrderingTable.TriangleKey(depths[0], depths[1], depths[2], RenderOrderingTable.DepthShift(RenderOrderingTable.DepthBucketShift))
            : RenderOrderingTable.QuadKey(depths[0], depths[1], depths[2], depths[3], RenderOrderingTable.DepthShift(RenderOrderingTable.DepthBucketShift));
        if (!_pageLayers.TryGetValue((model, polygon.TextureKey), out var page))
        {
            throw new InvalidOperationException($"battle painter page missing fighter={model.FighterId} texture={polygon.TextureKey}");
        }

        pending.Add(new PendingPolygon(key, world, polygon.Uvs, polygon.Colors, page, polygon.Abr == 0 ? 0.5f : 1f));
    }

    /// <summary>Synchronizes texture-array pages with the supplied models.</summary>
    /// <param name="models">Models whose texture pages are required.</param>
    private void EnsurePages(IReadOnlyList<BattleModel> models)
    {
        var known = BuildKnownPages(models);
        if (_pageLayers.Keys.Any(key => !known.Contains(key)))
        {
            _pageLayers.Clear();
            _dirtyPages = true;
            EnsurePages(models);
            return;
        }

        if (_dirtyPages)
        {
            BuildTextureArray();
        }
    }

    /// <summary>Registers model texture pages and returns the currently known page keys.</summary>
    /// <param name="models">Models whose texture pages are registered.</param>
    /// <returns>Set of model texture-page keys.</returns>
    private HashSet<(BattleModel Model, int TextureKey)> BuildKnownPages(IReadOnlyList<BattleModel> models)
    {
        var known = new HashSet<(BattleModel Model, int TextureKey)>();
        var pageIndex = _pageLayers.Count;
        foreach (var model in models)
        {
            model.FacePageChanged -= OnFacePageChanged;
            model.FacePageChanged += OnFacePageChanged;
            foreach (var (key, image) in model.EnumerateTexturePages())
            {
                if (image.GetWidth() != 256 || image.GetHeight() != 256)
                {
                    throw new InvalidOperationException($"battle painter texture is not 256x256 fighter={model.FighterId} key={key}");
                }

                known.Add((model, key));
                if (!_pageLayers.ContainsKey((model, key)))
                {
                    _pageLayers[(model, key)] = pageIndex++;
                    _dirtyPages = true;
                }
            }
        }

        return known;
    }

    /// <summary>Builds the GPU texture array from registered page images.</summary>
    private void BuildTextureArray()
    {
        var images = new Godot.Collections.Array<Image>();
        foreach (var pair in _pageLayers.OrderBy(pair => pair.Value))
        {
            images.Add(pair.Key.Model.EnumerateTexturePages().Single(value => value.TextureKey == pair.Key.TextureKey).Image);
        }

        if (images.Count == 0)
        {
            _pages = null;
            return;
        }

        _pages = new Texture2DArray();
        if (_pages.CreateFromImages(images) != Error.Ok)
        {
            throw new InvalidOperationException("battle painter could not build Texture2DArray");
        }

        _material.SetShaderParameter("pages", _pages);
        _dirtyPages = false;
    }

    /// <summary>Marks texture pages stale after face animation changes one page.</summary>
    /// <param name="_">Changed texture key.</param>
    /// <param name="__">Updated page image.</param>
    private void OnFacePageChanged(int _, Image __) => _dirtyPages = true;

    /// <summary>Emits the ordered pending polygons into the Godot mesh.</summary>
    /// <param name="pending">Polygons to emit.</param>
    private void EmitMesh(IReadOnlyList<PendingPolygon> pending)
    {
        var order = RenderOrderingTable.DrawOrder([.. pending.Select(polygon => polygon.Key)]);
        var vertices = new List<Godot.Vector3>();
        var uvs = new List<Godot.Vector2>();
        var uv2 = new List<Godot.Vector2>();
        var colors = new List<Color>();
        foreach (var index in order)
        {
            var polygon = pending[index];
            var indices = polygon.Positions.Length == 3 ? [0, 1, 2] : RenderOrderingTable.WholeQuadIndices().ToArray();
            foreach (var corner in indices)
            {
                vertices.Add(polygon.Positions[corner]);
                uvs.Add(polygon.Uvs[corner]);
                uv2.Add(new Godot.Vector2(polygon.Page, polygon.Alpha));
                // Texture2DArray layers are linear uploads whereas the prior ImageTexture path
                // is source-colour decoded. Keep the source 2x PS1 modulation in the shader and
                // compensate the upload-domain delta here, verified against the command-menu oracle.
                colors.Add(polygon.Colors[corner] * 0.9f);
            }
        }

        if (vertices.Count == 0)
        {
            _instance.Mesh = null;
            return;
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
        arrays[(int)Mesh.ArrayType.TexUV] = uvs.ToArray();
        arrays[(int)Mesh.ArrayType.TexUV2] = uv2.ToArray();
        arrays[(int)Mesh.ArrayType.Color] = colors.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        _instance.Mesh = mesh;
    }

    private const string ShaderCode = @"shader_type spatial;
render_mode unshaded, depth_test_disabled, depth_draw_never, cull_disabled, blend_mix;
uniform sampler2DArray pages : source_color, filter_nearest;
void fragment() {
    vec4 sampled = texture(pages, vec3(UV, UV2.x));
    if (sampled.a < 0.25) discard;
    ALBEDO = sampled.rgb * COLOR.rgb * 2.0;
    ALPHA = UV2.y;
}";

    /// <summary>Stores projected ordering and render data for one pending polygon.</summary>
    private readonly record struct PendingPolygon(int Key, Godot.Vector3[] Positions, Godot.Vector2[] Uvs, Color[] Colors, int Page, float Alpha);
}
