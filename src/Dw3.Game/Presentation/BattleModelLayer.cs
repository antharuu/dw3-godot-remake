using Godot;

namespace Dw3Production.Presentation;

/// <summary>
/// Draws source battle primitives as an explicitly ordered CanvasItem command stream. Each source
/// primitive remains whole while it receives its ordering-table key, then textured ABR primitives
/// emit adjacent non-STP and STP coverage commands into the same evolving framebuffer.
/// </summary>
public sealed class BattleModelLayer : IDisposable
{
    private const string OpaqueShader = "shader_type canvas_item; uniform int coverage; varying vec4 vertex_color; void vertex(){ vertex_color=COLOR; } void fragment(){ vec4 t=texture(TEXTURE,UV); if(t.a<0.25) discard; bool s=abs(t.a-0.501961)<0.01; if((coverage==1 && s)||(coverage==2 && !s)) discard; COLOR=vec4(t.rgb*vertex_color.rgb*2.0,1.0); }";
    private const string HalfShader = "shader_type canvas_item; render_mode blend_mix; varying vec4 vertex_color; void vertex(){ vertex_color=COLOR; } void fragment(){ vec4 t=texture(TEXTURE,UV); if(abs(t.a-0.501961)>=0.01) discard; COLOR=vec4(t.rgb*vertex_color.rgb*2.0,0.5); }";
    private const string AdditiveShader = "shader_type canvas_item; render_mode blend_add; varying vec4 vertex_color; void vertex(){ vertex_color=COLOR; } void fragment(){ vec4 t=texture(TEXTURE,UV); if(abs(t.a-0.501961)>=0.01) discard; COLOR=vec4(t.rgb*vertex_color.rgb*2.0,1.0); }";

    private readonly Node2D _root;
    private readonly ShaderMaterial _opaque;
    private readonly ShaderMaterial _nonStp;
    private readonly ShaderMaterial _half;
    private readonly ShaderMaterial _additive;
    private readonly List<Polygon2D> _commands = [];
    private readonly List<Polygon2D> _probeCalibration = [];
    private readonly List<(int Entry, int Command)> _emitted = [];
    private readonly List<(Node2D Node, int Entry)> _overlays = [];
    private ImageTexture? _probeQuadrants;
    private ImageTexture? _probeAlpha;
    private Polygon2D? _probeTriangle;

    /// <summary>Initializes a new instance of the <see cref="BattleModelLayer"/> class.</summary>
    /// <param name="parent">Canvas parent positioned directly over the stage and below the HUD.</param>
    /// <param name="name">Name assigned to the command-stream root.</param>
    public BattleModelLayer(Node parent, string name)
    {
        _root = new Node2D { Name = name, TextureFilter = CanvasItem.TextureFilterEnum.Nearest };
        parent.AddChild(_root);
        _opaque = CreateMaterial(OpaqueShader, 0);
        _nonStp = CreateMaterial(OpaqueShader, 1);
        _half = CreateMaterial(HalfShader, 0);
        _additive = CreateMaterial(AdditiveShader, 0);
    }

    /// <summary>Gets the total source polygons submitted to the latest rebuild.</summary>
    public int PolygonCount { get; private set; }

    /// <summary>Gets the source polygons rejected by the projected winding predicate.</summary>
    public int CulledCount { get; private set; }

    /// <summary>Gets the source polygons the GPU refuses after the GTE projection (a corner close to or behind the
    /// eye saturates the division and the polygon spans beyond the GPU limits).</summary>
    public int GpuRejectedCount { get; private set; }

    /// <summary>Gets unsupported ABR primitives. Observed modes zero and one never enter this count.</summary>
    public int UnsupportedAbrCount { get; private set; }

    /// <summary>Gets or sets optional offscreen probe interventions on the actual executor.</summary>
    internal BattleRasterControl? ProbeControl { get; set; }

    /// <summary>Places the raster command root immediately after a stage canvas sibling.</summary>
    /// <param name="sibling">Stage canvas item that must remain underneath the command stream.</param>
    public void MoveAfter(Node sibling)
    {
        var parent = _root.GetParent();
        parent.MoveChild(_root, sibling.GetIndex() + 1);
    }

    /// <summary>Clears every visible command while retaining pooled canvas nodes.</summary>
    public void Clear()
    {
        foreach (var command in _commands)
        {
            command.Visible = false;
        }

        PolygonCount = CulledCount = UnsupportedAbrCount = GpuRejectedCount = 0;
    }

    /// <summary>Adds a canvas node drawn inside this layer's ordering table, such as a models-layer sprite effect.</summary>
    /// <param name="node">Node to adopt; it is ordered by the next <see cref="PlaceOverlays"/>.</param>
    public void AddOverlay(Node2D node) => _root.AddChild(node);

    /// <summary>
    /// Sorts overlay nodes into the emitted polygon stream by their source ordering-table entries, and keeps
    /// that placement for every later rebuild until the next call.
    /// </summary>
    /// <param name="overlays">Overlay nodes in their own draw order (back to front) with their entries.</param>
    public void PlaceOverlays(IReadOnlyList<(Node2D Node, int Entry)> overlays)
    {
        ArgumentNullException.ThrowIfNull(overlays);
        _overlays.Clear();
        _overlays.AddRange(overlays);
        ApplyOverlays();
    }

    /// <summary>Shows or hides the production Canvas calibration fixture used by the offscreen model probe.</summary>
    /// <param name="visible">Whether the synthetic fixture participates in the live canvas command stream.</param>
    /// <param name="legacyDoubleUv">Whether to intentionally reproduce the pre-repair double UV conversion.</param>
    public void SetProbeCanvasCalibrationVisible(bool visible, bool legacyDoubleUv)
    {
        if (!visible)
        {
            SetProbeCalibrationVisibility(false);
            return;
        }

        if (_probeCalibration.Count == 0)
        {
            CreateProbeCalibration(legacyDoubleUv);
        }

        SetProbeCalibrationVisibility(true);
    }

    /// <summary>Shows a probe-only opaque red triangle in this layer's actual canvas parent.</summary>
    /// <param name="visible">Whether the diagnostic primitive is visible.</param>
    public void SetProbeDiagnosticTriangleVisible(bool visible)
    {
        if (!visible)
        {
            if (_probeTriangle is { } triangle)
            {
                triangle.Visible = false;
            }

            return;
        }

        _probeTriangle ??= CreateProbeTriangle();
        _probeTriangle.Visible = true;
    }

    /// <summary>Rebuilds one shared source-primitive command stream for a camera and fighter set.</summary>
    /// <param name="camera">Camera that supplies the unchanged source projection.</param>
    /// <param name="models">Visible model copies contributing source primitives.</param>
    public void Rebuild(Camera3D camera, IReadOnlyList<BattleModel> models)
    {
        var pending = Collect(camera, models);
        var order = BattlePrimitiveCompositor.Order([.. pending.Select(item => new SourcePrimitive(item.Key, item.Polygon.Size))]);
        Emit(pending, order);
        foreach (var model in models)
        {
            model.SetPainterLayerActive(true);
        }
    }

    /// <summary>Releases canvas resources owned by this layer.</summary>
    public void Dispose()
    {
        foreach (var command in _commands)
        {
            command.QueueFree();
        }

        _opaque.Dispose();
        _nonStp.Dispose();
        _half.Dispose();
        _additive.Dispose();
        _probeQuadrants?.Dispose();
        _probeAlpha?.Dispose();
        _root.QueueFree();
    }

    /// <summary>Converts the retained normalized mesh UV contract to Polygon2D texture pixels.</summary>
    /// <param name="normalizedUvs">UVs stored as source texel values divided by 256 for the 3-D mesh path.</param>
    /// <param name="texture">Canvas page that supplies the actual pixel dimensions.</param>
    /// <returns>UVs in the pixel units required by <see cref="Polygon2D"/>.</returns>
    private static Vector2[] ToCanvasPixels(IReadOnlyList<Vector2> normalizedUvs, Texture2D texture) =>
        [.. normalizedUvs.Select(uv => new Vector2(uv.X * texture.GetWidth(), uv.Y * texture.GetHeight()))];

    /// <summary>Converts a projected Godot canvas point to the ordering helper's numeric vector.</summary>
    /// <param name="value">Projected canvas point.</param>
    /// <returns>The same point for source winding evaluation.</returns>
    private static System.Numerics.Vector2 ToNumerics(Vector2 value) => new(value.X, value.Y);

    /// <summary>Calculates the PS1 ordering-table key without splitting a quad into its raster triangles.</summary>
    /// <param name="depth">View depths of the source corners.</param>
    /// <returns>The source ordering-table key.</returns>
    private static int OrderKey(int[] depth)
    {
        var shift = RenderOrderingTable.DepthShift(RenderOrderingTable.DepthBucketShift);
        return depth.Length == 3
            ? RenderOrderingTable.TriangleKey(depth[0], depth[1], depth[2], shift)
            : RenderOrderingTable.QuadKey(depth[0], depth[1], depth[2], depth[3], shift);
    }

    /// <summary>Gets the child index that puts a moved overlay directly before another child.</summary>
    /// <param name="node">Overlay being moved.</param>
    /// <param name="before">Child the overlay must precede.</param>
    /// <returns>The target index once the overlay has left its current slot.</returns>
    private static int OverlayIndex(Node node, Node before) => before.GetIndex() - (node.GetIndex() < before.GetIndex() ? 1 : 0);

    /// <summary>Builds the nearest-filtered source fixture that exposes all four texture quadrants.</summary>
    /// <returns>A mutable two-by-two fixture with opaque red, green, blue and white texels.</returns>
    private static ImageTexture CreateQuadrantTexture()
    {
        var image = Image.CreateEmpty(2, 2, false, Image.Format.Rgba8);
        image.SetPixel(0, 0, new Color(64f / 255f, 0, 0));
        image.SetPixel(1, 0, new Color(0, 64f / 255f, 0));
        image.SetPixel(0, 1, new Color(0, 0, 64f / 255f));
        image.SetPixel(1, 1, new Color(64f / 255f, 64f / 255f, 64f / 255f));
        return ImageTexture.CreateFromImage(image);
    }

    /// <summary>Builds raw alpha zero, STP 128 and non-STP 255 texels for the paired coverage passes.</summary>
    /// <returns>A three-pixel fixture whose alpha classes are retained by the production shaders.</returns>
    private static ImageTexture CreateAlphaTexture()
    {
        var image = Image.CreateEmpty(3, 1, false, Image.Format.Rgba8);
        image.SetPixel(0, 0, new Color(1, 1, 1, 0));
        image.SetPixel(1, 0, new Color(1, 1, 1, 128f / 255f));
        image.SetPixel(2, 0, Colors.White);
        return ImageTexture.CreateFromImage(image);
    }

    /// <summary>Converts normalized source UVs once, with an explicitly probe-only legacy path for RED evidence.</summary>
    /// <param name="texture">Fixture texture supplying the pixel dimensions.</param>
    /// <param name="legacyDoubleUv">Whether to apply the intentionally incorrect second conversion.</param>
    /// <returns>Canvas UVs for the fixture corners.</returns>
    private static Vector2[] CalibrationUvs(Texture2D texture, bool legacyDoubleUv)
    {
        var once = ToCanvasPixels([Vector2.Zero, Vector2.Right, Vector2.One, Vector2.Down], texture);
        return legacyDoubleUv ? ToCanvasPixels(once, texture) : once;
    }

    /// <summary>Builds synthetic Canvas primitives through the same Polygon2D materials as fighter commands.</summary>
    /// <param name="legacyDoubleUv">Whether to deliberately apply the obsolete second texture-size conversion.</param>
    private void CreateProbeCalibration(bool legacyDoubleUv)
    {
        _probeQuadrants = CreateQuadrantTexture();
        _probeAlpha = CreateAlphaTexture();
        AddCalibrationQuad(new Rect2(16, 16, 32, 32), _probeQuadrants, new Color(128f / 255f, 128f / 255f, 128f / 255f), _opaque, legacyDoubleUv);
        AddCalibrationQuad(new Rect2(64, 16, 32, 32), _probeQuadrants, Colors.White, _opaque, legacyDoubleUv);
        AddCalibrationQuad(new Rect2(16, 64, 48, 16), _probeAlpha, Colors.White, _nonStp, legacyDoubleUv);
        AddCalibrationQuad(new Rect2(16, 64, 48, 16), _probeAlpha, Colors.White, _half, legacyDoubleUv);
    }

    /// <summary>Adds one synthetic Polygon2D command with source-normalized UVs converted exactly once.</summary>
    /// <param name="rect">Canvas destination rectangle.</param>
    /// <param name="texture">Fixture texture consumed by the production shader.</param>
    /// <param name="color">Vertex RGB modulation supplied to all four corners.</param>
    /// <param name="material">Production coverage material selected for this fixture.</param>
    /// <param name="legacyDoubleUv">Whether to reproduce the obsolete second texture-size conversion.</param>
    private void AddCalibrationQuad(Rect2 rect, Texture2D texture, Color color, ShaderMaterial material, bool legacyDoubleUv)
    {
        var node = new Polygon2D
        {
            Name = $"BattleCanvasCalibration{_probeCalibration.Count}",
            Polygon = [rect.Position, new Vector2(rect.End.X, rect.Position.Y), rect.End, new Vector2(rect.Position.X, rect.End.Y)],
            Polygons = [(int[])[0, 1, 2], (int[])[2, 3, 0]],
            UV = CalibrationUvs(texture, legacyDoubleUv),
            VertexColors = [color, color, color, color],
            Texture = texture,
            Material = material,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
        };
        _root.AddChild(node);
        _probeCalibration.Add(node);
    }

    /// <summary>Changes only the synthetic fixture visibility, retaining the production command pool.</summary>
    /// <param name="visible">Whether all calibration commands should draw.</param>
    private void SetProbeCalibrationVisibility(bool visible)
    {
        foreach (var command in _probeCalibration)
        {
            command.Visible = visible;
        }
    }

    /// <summary>Builds the deliberately untextured control primitive used to prove canvas visibility.</summary>
    /// <returns>A solid red triangle that cannot depend on model UVs, alpha, or shaders.</returns>
    private Polygon2D CreateProbeTriangle()
    {
        var triangle = new Polygon2D
        {
            Name = "BattleModelProbeTriangle",
            Polygon = [new Vector2(32, 32), new Vector2(192, 32), new Vector2(32, 192)],
            Color = Colors.Red,
        };
        _root.AddChild(triangle);
        return triangle;
    }

    /// <summary>Collects and validates one source primitive stream before any draw is emitted.</summary>
    /// <param name="camera">Camera used for projection and ordering depth.</param>
    /// <param name="models">Models contributing primitives.</param>
    /// <returns>Unsplit primitives in source submission order.</returns>
    private List<BattleRasterPrimitive> Collect(Camera3D camera, IReadOnlyList<BattleModel> models)
    {
        PolygonCount = CulledCount = UnsupportedAbrCount = GpuRejectedCount = 0;
        var result = new List<BattleRasterPrimitive>();
        foreach (var model in models.Where(model => model.Visible))
        {
            CollectModel(camera, model, result);
        }

        PolygonCount = result.Count;
        return result;
    }

    /// <summary>Collects visible, camera-facing primitives from one model.</summary>
    /// <param name="camera">Camera used for source-compatible projection.</param>
    /// <param name="model">Model supplying primitives and texture pages.</param>
    /// <param name="result">Destination stream.</param>
    private void CollectModel(Camera3D camera, BattleModel model, List<BattleRasterPrimitive> result)
    {
        foreach (var polygon in model.EnumeratePolygons())
        {
            if (!model.IsBoneVisible(polygon.Bone))
            {
                continue;
            }

            if (polygon.Abr is > 1)
            {
                UnsupportedAbrCount++;
                continue;
            }

            var world = polygon.Positions.Select(position => model.WorldPosition(polygon.Bone, position)).ToArray();
            if (!Project(camera, world, out var screen))
            {
                GpuRejectedCount++;
                continue;
            }

            if (!BattlePrimitiveCompositor.FacesCameraInSourceOrder(
                ToNumerics(screen[0]),
                ToNumerics(screen[1]),
                ToNumerics(screen[2]))
                || (polygon.Size == 4 && !RenderOrderingTable.FacesCamera(
                    ToNumerics(screen[0]),
                    ToNumerics(screen[1]),
                    ToNumerics(screen[2]),
                    ToNumerics(screen[3]))))
            {
                CulledCount++;
                continue;
            }

            int[] depth = ViewDepths(camera, world);
            result.Add(new BattleRasterPrimitive(OrderKey(depth), polygon, screen, model.TextureFor(polygon.TextureKey), model.FighterId) { Entry = BattleSpriteOrdering.PolygonEntry(depth) });
        }
    }

    /// <summary>Projects a polygon through the source hardware rules, or the unfiltered camera projection when an
    /// offscreen probe asks for the previous behaviour as its paired control.</summary>
    /// <param name="camera">Viewport camera.</param>
    /// <param name="world">World positions of the polygon corners.</param>
    /// <param name="screen">Receives the viewport corners.</param>
    /// <returns>Whether the polygon is drawn.</returns>
    private bool Project(Camera3D camera, Vector3[] world, out Vector2[] screen)
    {
        if (ProbeControl?.UnfilteredProjection == true)
        {
            screen = [.. world.Select(camera.UnprojectPosition)];
            return true;
        }

        return BattleGteScreen.TryProject(camera, world, out screen);
    }

    /// <summary>Projects the source corners to their view depths in source units.</summary>
    /// <param name="camera">Camera defining depth.</param>
    /// <param name="world">World positions of source corners.</param>
    /// <returns>One view depth per corner.</returns>
    private int[] ViewDepths(Camera3D camera, IReadOnlyList<Vector3> world)
    {
        GC.KeepAlive(_root);
        var inverse = camera.GlobalTransform.AffineInverse();
        return [.. world.Select(position => (int)MathF.Round(-(inverse * position).Z * BattleSceneMath.Ps1UnitsPerGodotUnit))];
    }

    /// <summary>Emits ordered opaque and STP coverage commands into the single canvas framebuffer.</summary>
    /// <param name="pending">Collected whole source primitives.</param>
    /// <param name="order">Indices of <paramref name="pending"/> in source draw order.</param>
    private void Emit(List<BattleRasterPrimitive> pending, IReadOnlyList<int> order)
    {
        var ordered = order.Select(index => pending[index]);
        if (ProbeControl is { } control)
        {
            control.Ordered = control.Fixture ?? [.. ordered];
            ordered = control.Ordered.Take(control.Limit).Where(control.Includes);
        }

        var command = 0;
        _emitted.Clear();
        foreach (var primitive in ordered)
        {
            _emitted.Add((primitive.Entry, command));
            command = EmitPrimitive(primitive, command);
        }

        HideUnused(command);
        ApplyOverlays();
    }

    /// <summary>Emits the disjoint coverage commands belonging to one ordered source primitive.</summary>
    /// <param name="primitive">Primitive whose quad grouping has already determined global order.</param>
    /// <param name="command">Next pooled canvas command slot.</param>
    /// <returns>The following free canvas command slot.</returns>
    private int EmitPrimitive(BattleRasterPrimitive primitive, int command)
    {
        if (primitive.Polygon.Abr < 0 || ProbeControl?.UsesWrongBlend(primitive) == true)
        {
            Draw(command++, primitive, _opaque);
            return command;
        }

        Draw(command++, primitive, _nonStp);
        Draw(command++, primitive, primitive.Polygon.Abr == 0 ? _half : _additive);
        return command;
    }

    /// <summary>Configures one pooled Polygon2D command without allocating a framebuffer or readback.</summary>
    /// <param name="index">Stable canvas command index.</param>
    /// <param name="primitive">Projected primitive and its mutable page texture.</param>
    /// <param name="material">Fixed-function blend material for this disjoint coverage pass.</param>
    private void Draw(int index, BattleRasterPrimitive primitive, ShaderMaterial material)
    {
        var node = CommandAt(index);
        node.Polygon = [.. primitive.Screen];
        node.Polygons = primitive.Polygon.Size == 4
            ? [(int[])[0, 1, 2], (int[])[2, 1, 3]]
            : [(int[])[0, 1, 2]];
        if (ProbeControl?.ClearIndices == true)
        {
            node.Polygons = [];
        }

        node.UV = ToCanvasPixels(primitive.Polygon.Uvs, primitive.Texture);
        node.VertexColors = primitive.Polygon.Colors;
        node.Texture = primitive.Texture;
        node.Material = material;
        node.Visible = true;
    }

    /// <summary>
    /// Moves each live overlay just before the first emitted polygon it is drawn under, from the nearest overlay
    /// to the farthest, so overlays sharing a position keep their own order. Overlays drawn after every
    /// polygon move to the end of the stream.
    /// </summary>
    private void ApplyOverlays()
    {
        _overlays.RemoveAll(overlay => !GodotObject.IsInstanceValid(overlay.Node) || overlay.Node.GetParent() != _root);
        int[] entries = [.. _emitted.Select(item => item.Entry)];
        var heads = new Dictionary<int, Node>();
        for (int i = _overlays.Count - 1; i >= 0; i--)
        {
            Node2D node = _overlays[i].Node;
            int point = BattleSpriteOrdering.InsertionPoint(entries, _overlays[i].Entry);
            Node? before = heads.GetValueOrDefault(point);
            if (before is null && point < _emitted.Count)
            {
                before = _commands[_emitted[point].Command];
            }

            _root.MoveChild(node, before is null ? _root.GetChildCount() - 1 : OverlayIndex(node, before));
            heads[point] = node;
        }
    }

    /// <summary>Gets or creates one reusable canvas command node.</summary>
    /// <param name="index">Zero-based command position.</param>
    /// <returns>The reusable polygon node.</returns>
    private Polygon2D CommandAt(int index)
    {
        while (_commands.Count <= index)
        {
            var node = new Polygon2D
            {
                Name = $"BattleRasterCommand{_commands.Count}",
                Antialiased = false,
                TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            };
            _root.AddChild(node);
            _commands.Add(node);
        }

        return _commands[index];
    }

    /// <summary>Hides pooled commands beyond the current source stream length.</summary>
    /// <param name="used">Number of commands emitted this frame.</param>
    private void HideUnused(int used)
    {
        for (var index = used; index < _commands.Count; index++)
        {
            _commands[index].Visible = false;
        }
    }

    /// <summary>Creates one nearest-neighbour canvas shader and configures an opaque coverage selector.</summary>
    /// <param name="code">Canvas shader source.</param>
    /// <param name="coverage">Zero all, one non-STP only, two STP only.</param>
    /// <returns>The configured shader material.</returns>
    private ShaderMaterial CreateMaterial(string code, int coverage)
    {
        GC.KeepAlive(_root);
        var material = new ShaderMaterial { Shader = new Shader { Code = code } };
        material.SetShaderParameter("coverage", coverage);
        return material;
    }
}
