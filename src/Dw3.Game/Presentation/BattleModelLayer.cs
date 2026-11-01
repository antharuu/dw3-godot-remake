using Godot;

namespace Dw3Production.Presentation;

/// <summary>
/// The PS1 BATTLE_LAYER_MODELS equivalent. the decoded mesh contract adds both fighters' primitives to one
/// otShift=12 ordering table, then draws that layer over the z-buffered stage. This node rebuilds
/// the one mesh each Present because the PS1 key is based on the posed, projected vertices.
/// </summary>
public sealed class BattleModelLayer
{
    private readonly MeshInstance3D _instance;
    private readonly ShaderMaterial _material;
    private readonly Dictionary<(BattleModel Model, int TextureKey), int> _pageLayers = [];
    private Texture2DArray? _pages;
    private bool _dirtyPages = true;

    public int PolygonCount { get; private set; }
    public int CulledCount { get; private set; }
    public int AbrUnsortedCount { get; private set; }

    public BattleModelLayer(Node3D parent, string name)
    {
        _instance = new MeshInstance3D { Name = name, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        _material = new ShaderMaterial { Shader = new Shader { Code = ShaderCode } };
        _instance.MaterialOverride = _material;
        _material.RenderPriority = 10;
        parent.AddChild(_instance);
    }

    public void Clear()
    {
        _pageLayers.Clear();
        _pages = null;
        _instance.Mesh = null;
        _dirtyPages = true;
        PolygonCount = CulledCount = AbrUnsortedCount = 0;
    }

    public void Rebuild(Camera3D camera, IReadOnlyList<BattleModel> models)
    {
        EnsurePages(models);
        var pending = new List<PendingPolygon>();
        PolygonCount = CulledCount = AbrUnsortedCount = 0;
        foreach (var model in models)
        {
            if (!model.Visible) continue;
            foreach (var polygon in model.EnumeratePolygons())
            {
                if (!model.IsBoneVisible(polygon.Bone)) continue;
                // -1 is opaque. 0 is the source's first semi-transparent mode, stored in
                // MeshDrawState.abr as 1. Modes 1..3 retain their existing surfaces.
                if (polygon.Abr > 0) { AbrUnsortedCount++; continue; }
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
                var cameraInverse = camera.GlobalTransform.AffineInverse();
                var depths = world.Select(position => (int)MathF.Round(-(cameraInverse * position).Z * BattleSceneMath.Ps1UnitsPerGodotUnit)).ToArray();
                var key = polygon.Size == 3
                    ? RenderOrderingTable.TriangleKey(depths[0], depths[1], depths[2], RenderOrderingTable.DepthShift(RenderOrderingTable.DepthBucketShift))
                    : RenderOrderingTable.QuadKey(depths[0], depths[1], depths[2], depths[3], RenderOrderingTable.DepthShift(RenderOrderingTable.DepthBucketShift));
                if (!_pageLayers.TryGetValue((model, polygon.TextureKey), out var page))
                    throw new InvalidOperationException($"battle painter page missing fighter={model.FighterId} texture={polygon.TextureKey}");
                pending.Add(new PendingPolygon(key, world, polygon.Uvs, polygon.Colors, page, polygon.Abr == 0 ? 0.5f : 1f));
            }
        }
        PolygonCount = pending.Count;
        EmitMesh(pending);
        foreach (var model in models) model.SetPainterLayerActive(true);
    }

    private void EnsurePages(IReadOnlyList<BattleModel> models)
    {
        var pages = new List<Image>();
        var known = new HashSet<(BattleModel, int)>();
        foreach (var model in models)
        {
            model.FacePageChanged -= OnFacePageChanged;
            model.FacePageChanged += OnFacePageChanged;
            foreach (var (key, image) in model.EnumerateTexturePages())
            {
                if (image.GetWidth() != 256 || image.GetHeight() != 256)
                    throw new InvalidOperationException($"battle painter texture is not 256x256 fighter={model.FighterId} key={key}");
                known.Add((model, key));
                if (!_pageLayers.ContainsKey((model, key)))
                {
                    _pageLayers[(model, key)] = pages.Count;
                    pages.Add(image);
                    _dirtyPages = true;
                }
            }
        }
        if (_pageLayers.Keys.Any(key => !known.Contains(key)))
        {
            _pageLayers.Clear();
            _dirtyPages = true;
            EnsurePages(models);
            return;
        }
        if (_dirtyPages)
        {
            var images = new Godot.Collections.Array<Image>();
            foreach (var pair in _pageLayers.OrderBy(pair => pair.Value))
                images.Add(pair.Key.Model.EnumerateTexturePages().Single(value => value.TextureKey == pair.Key.TextureKey).Image);
            if (images.Count == 0) { _pages = null; return; }
            _pages = new Texture2DArray();
            if (_pages.CreateFromImages(images) != Error.Ok)
                throw new InvalidOperationException("battle painter could not build Texture2DArray");
            _material.SetShaderParameter("pages", _pages);
            _dirtyPages = false;
        }
    }

    private void OnFacePageChanged(int _, Image __) => _dirtyPages = true;

    private void EmitMesh(IReadOnlyList<PendingPolygon> pending)
    {
        var order = RenderOrderingTable.DrawOrder([.. pending.Select(polygon => polygon.Key)]);
        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var uv2 = new List<Vector2>();
        var colors = new List<Color>();
        foreach (var index in order)
        {
            var polygon = pending[index];
            var indices = polygon.Positions.Length == 3 ? [0, 1, 2] : RenderOrderingTable.WholeQuadIndices().ToArray();
            foreach (var corner in indices)
            {
                vertices.Add(polygon.Positions[corner]);
                uvs.Add(polygon.Uvs[corner]);
                uv2.Add(new Vector2(polygon.Page, polygon.Alpha));
                // Texture2DArray layers are linear uploads whereas the prior ImageTexture path
                // is source-colour decoded. Keep the source 2x PS1 modulation in the shader and
                // compensate the upload-domain delta here, verified against the command-menu oracle.
                colors.Add(polygon.Colors[corner] * 0.9f);
            }
        }
        if (vertices.Count == 0) { _instance.Mesh = null; return; }
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

    private readonly record struct PendingPolygon(int Key, Vector3[] Positions, Vector2[] Uvs, Color[] Colors, int Page, float Alpha);

    private const string ShaderCode = @"shader_type spatial;
render_mode unshaded, depth_test_disabled, depth_draw_never, cull_disabled, blend_mix;
uniform sampler2DArray pages : source_color, filter_nearest;
void fragment() {
    vec4 sampled = texture(pages, vec3(UV, UV2.x));
    if (sampled.a < 0.25) discard;
    ALBEDO = sampled.rgb * COLOR.rgb * 2.0;
    ALPHA = UV2.y;
}";
}
