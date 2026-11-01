using Godot;
using System.Text.Json;

namespace Dw3Production.Presentation;

/// <summary>
/// A decoded DW3 battle model, loaded from the external disc conversion and built as a
/// Godot 3D bone hierarchy with textured meshes. It reproduces the proven BattleProbe path:
/// the model JSON's bone records, per-bone mesh streams (triangles from source quads), and the
/// three key channels (position/rotation/scale) sampled at a frame by linear interpolation.
/// Rotation uses the PS1 angle unit pi/2048 and ZYX Euler order.
///
/// The TIM entries are raw VRAM uploads. Their polygon-selected tpage mode and CLUT, rather than
/// the TIM header's mode label, define the 256x256 textures consumed here. The motion archive
/// holds every motion (not just motion 1), so an attack/technique pose can be shown.
/// </summary>
public sealed class BattleModel
{
    private readonly Dictionary<int, Node3D> _bones = [];
    private readonly Dictionary<int, List<Vector4>[]> _tracks = [];
    private readonly List<List<List<int>>> _motions = [];
    private readonly List<ShaderMaterial> _materials = [];
    private readonly Dictionary<int, (Vector3 Position, Vector3 Rotation, Vector3 Scale)> _blendPose = [];
    private readonly List<LitSurface> _litSurfaces = [];
    private readonly List<SourcePolygonState> _sourcePolygons = [];
    private readonly Dictionary<int, FaceTexture> _faceTextures = [];
    private readonly List<FacePart> _faceParts = [];
    private bool _painterLayerActive;
    private Func<int> _faceRandom = () => Random.Shared.Next();
    private int _blinkTimer;
    private int _faceTime;
    private BattleLightSet? _lightSet;
    private Color? _modelColor;
    private string _lightingVertexSummary = "vertex=unavailable";

    public Color? StageBgColor { get; private set; }

    public Node3D Root { get; }
    public bool Visible => Root.Visible;
    public int BoneCount { get; private set; }
    public int MeshCount { get; private set; }
    /// <summary>Polygons whose source 0x9 command disabled new UV loading but which still
    /// render through the residual POLY_FT/GT texture state.</summary>
    public int UntexturedPolygonCount { get; private set; }
    /// <summary>The model's motions, index 0 = motion 1 (the idle). Each is a list of steps
    /// <c>[index, count, frame, endFrame]</c> ending on the 0x7FFF terminator.</summary>
    public IReadOnlyList<IReadOnlyList<IReadOnlyList<int>>> Motions => _motions;
    /// <summary>The fighter's camera views (the decoded fighter-camera record contract / the decoded battle-camera record contract), in PS1
    /// integer units: <c>[x, y, z]</c> position, reference and a projection distance. Empty
    /// for a fight stage (which has no fighter camera).</summary>
    public IReadOnlyList<BattleCameraView> Cameras { get; private set; } = [];
    public int FighterId { get; private set; }
    public int Distance { get; private set; }
    public int Height { get; private set; }
    public BattleMotionMachine? Motion { get; private set; }
    public BattleLightSet? StageLights { get; private set; }

    /// <summary>One decoded fighter camera view.</summary>
    public readonly record struct BattleCameraView(int[] CamPos, int[] CamRef, int Proj);
    public readonly record struct BattleLight(Vector3 Direction, Color Rgb);
    public readonly record struct BattleLightSet(IReadOnlyList<BattleLight> Lights, Color Ambient);
    private readonly record struct LightingVertex(Vector3 Normal, bool Lit, Color Flat);
    private sealed class LitSurface
    {
        public MeshInstance3D Instance { get; }
        public int Bone { get; }
        public Vector3[] Positions { get; }
        public Vector2[] Uvs { get; }
        public LightingVertex[] Lighting { get; }
        public ShaderMaterial Material { get; }
        public Color[] Colors { get; }
        /// <summary>Raw MeshDrawState.abr: 0 means opaque, 1..4 select PSX ABR 0..3.</summary>
        public int Abr { get; }

        public LitSurface(MeshInstance3D instance, int bone, Vector3[] positions, Vector2[] uvs,
            LightingVertex[] lighting, ShaderMaterial material, int abr)
        {
            Instance = instance;
            Bone = bone;
            Positions = positions;
            Uvs = uvs;
            Lighting = lighting;
            Material = material;
            Colors = new Color[positions.Length];
            Abr = abr;
        }
    }
    private sealed record SourcePolygonState(LitSurface Surface, int Bone, int Command, int VertexStart, int Size, int TextureKey, int Abr);
    private sealed record FaceTexture(Image Source, Image Working, ImageTexture Texture);
    private readonly record struct FacePart(int Index, int X, int Y, int W, int H, Vector2I[] Frames);
    /// <summary>One source primitive in the decoded mesh contract order, retaining its un-split quad and the
    /// colours computed by the most recent <see cref="RefreshLighting"/>.</summary>
    public readonly record struct SourcePolygon(int Bone, int VertexStart, int Size, int TextureKey, int Abr,
        Vector3[] Positions, Vector2[] Uvs, Color[] Colors);

    private BattleModel(Node3D root)
    {
        Root = root;
    }

    /// <summary>Loads a battle-model conversion from <paramref name="jsonPath"/>, building the
    /// model hierarchy as a child of <paramref name="parent"/>. The texture images live beside
    /// the JSON. Rejects a missing file, wrong format or a changed motion-1 invariant.</summary>
    public static BattleModel Load(Node3D parent, string jsonPath)
    {
        if (!File.Exists(jsonPath))
            throw new InvalidOperationException($"battle model JSON missing: {jsonPath}");
        using var document = JsonDocument.Parse(File.ReadAllText(jsonPath));
        var root = document.RootElement;
        if (root.GetProperty("format").GetString() != "dw3-battle-model-v8")
            throw new InvalidOperationException("unexpected external battle model format");
        // Motion 1 always starts at step index 0 and ends on the 0x7FFF terminator; the
        // step body is fighter-specific, so only the two universal invariants are pinned.
        var invariants = root.GetProperty("source_invariants");
        var first = invariants.GetProperty("motion_1_first_step");
        if (first.GetArrayLength() < 4 || first[0].GetInt32() != 0)
            throw new InvalidOperationException("battle model motion-1 does not start at index 0");
        var terminator = invariants.GetProperty("motion_1_terminator");
        if (terminator.GetArrayLength() < 4 || terminator[0].GetInt32() != 32767)
            throw new InvalidOperationException("battle model motion-1 terminator changed");

        var model = new BattleModel(new Node3D { Name = "BattleModel" });
        parent.AddChild(model.Root);
        if (root.TryGetProperty("lights", out var lightsElement))
            model.StageLights = ReadLightSet(lightsElement);
        if (root.TryGetProperty("bg_color", out var bg))
            model.StageBgColor = new Color(bg[0].GetInt32() / 255f, bg[1].GetInt32() / 255f, bg[2].GetInt32() / 255f);
        if (root.TryGetProperty("fighter_id", out var fighterId))
        {
            model.FighterId = fighterId.GetInt32();
            var geometry = root.GetProperty("geometry");
            model.Distance = geometry.GetProperty("distance").GetInt32();
            model.Height = geometry.GetProperty("height").GetInt32();
        }

        foreach (var bone in root.GetProperty("bones").EnumerateArray())
        {
            var index = bone.GetProperty("index").GetInt32();
            var node = new Node3D { Name = $"Bone{index}" };
            model._bones[index] = node;
            if (bone.GetProperty("parent").ValueKind == JsonValueKind.Null) model.Root.AddChild(node);
            else model._bones[bone.GetProperty("parent").GetInt32()].AddChild(node);
        }

        foreach (var track in root.GetProperty("tracks").EnumerateObject())
        {
            var sets = new List<Vector4>[3];
            var channelIndex = 0;
            foreach (var channel in track.Value.EnumerateArray())
            {
                var keys = new List<Vector4>();
                foreach (var key in channel.EnumerateArray())
                    keys.Add(new Vector4(key[1].GetSingle(), key[2].GetSingle(), key[3].GetSingle(), key[0].GetSingle()));
                sets[channelIndex++] = keys;
            }
            if (channelIndex != 3) throw new InvalidOperationException("battle model key track is not three channels");
            model._tracks.Add(int.Parse(track.Name), sets);
        }

        // Each texture is one consumer-selected PS1 tpage/CLUT combination. The converter has
        // already reconstructed local VRAM and decoded its 256x256 texels.
        var textures = root.GetProperty("textures");
        if (textures.GetArrayLength() == 0) throw new InvalidOperationException("battle model has no textures");
        var decodedTextures = LoadTextures(textures, Path.GetDirectoryName(jsonPath) ?? string.Empty);
        foreach (var texture in decodedTextures) model._faceTextures.Add(texture.Key, texture.Value);
        model.ConfigureFace(root, decodedTextures);

        foreach (var motion in root.GetProperty("motions").EnumerateArray())
        {
            var steps = new List<List<int>>();
            foreach (var step in motion.EnumerateArray())
                steps.Add([step[0].GetInt32(), step[1].GetInt32(), step[2].GetInt32(), step[3].GetInt32()]);
            model._motions.Add(steps);
        }
        if (model._motions.Count == 0) throw new InvalidOperationException("battle model has no motions");
        model.Motion = new BattleMotionMachine(model._motions);

        // The fighter cameras (absent from a fight stage): each is camPos/camRef (3 ints) + proj.
        if (root.TryGetProperty("cameras", out var camerasElement))
        {
            var cameras = new List<BattleCameraView>();
            foreach (var camera in camerasElement.EnumerateArray())
            {
                cameras.Add(new BattleCameraView(
                    ReadTriple(camera.GetProperty("camPos")),
                    ReadTriple(camera.GetProperty("camRef")),
                    camera.GetProperty("proj").GetInt32()));
            }
            model.Cameras = cameras;
        }

        var meshCount = 0;
        foreach (var mesh in root.GetProperty("meshes").EnumerateArray())
        {
            var bone = mesh.GetProperty("bone").GetInt32();
            var vertices = new List<Vector3>();
            foreach (var vertex in mesh.GetProperty("vertices").EnumerateArray())
                vertices.Add(Ps1Position(new Vector3(vertex[0].GetSingle(), vertex[1].GetSingle(), vertex[2].GetSingle())));
            var normals = new List<Vector3>();
            foreach (var normal in mesh.GetProperty("normals").EnumerateArray())
                normals.Add(new Vector3(normal[0].GetSingle(), -normal[1].GetSingle(), -normal[2].GetSingle()).Normalized());
            var groups = new Dictionary<(int Texture, int Abr), List<(JsonElement Polygon, int Command)>>();
            var command = 0;
            foreach (var polygon in mesh.GetProperty("polygons").EnumerateArray())
            {
                // the decoded mesh contract:590-624 copies UV/tpage/CLUT into POLY_FT/GT even
                // when state.textured is false. The converter has therefore
                // preserved the preceding textured command's residual UV/tpage
                // state, while PolygonColor retains the source flat/Gouraud RGB.
                if (polygon.GetProperty("uv").ValueKind == JsonValueKind.Null)
                    throw new InvalidOperationException("battle model polygon lacks residual UV state");
                if (!polygon.GetProperty("textured").GetBoolean()) model.UntexturedPolygonCount++;
                var key = (polygon.GetProperty("texture_key").GetInt32(), polygon.GetProperty("abr").GetInt32());
                if (!groups.TryGetValue(key, out var polygons)) groups[key] = polygons = [];
                polygons.Add((polygon, command++));
            }
            foreach (var (key, polygons) in groups)
            {
                if (!decodedTextures.TryGetValue(key.Texture, out var texture))
                    throw new InvalidOperationException($"battle model texture key {key.Texture} is missing");
                var surface = new SurfaceTool();
                surface.Begin(Mesh.PrimitiveType.Triangles);
                var surfacePositions = new List<Vector3>();
                var surfaceUvs = new List<Vector2>();
                var surfaceLighting = new List<LightingVertex>();
                var sourceStates = new List<(int Command, int VertexStart, int Size)>();
                foreach (var (polygon, sourceCommand) in polygons)
                {
                    var indices = polygon.GetProperty("indices");
                    var uv = polygon.GetProperty("uv");
                    var count = indices.GetArrayLength();
                    if (count != 3 && count != 4) throw new InvalidOperationException("battle model polygon is not triangle or quad");
                    var order = count == 3 ? new[] { 0, 1, 2 } : [0, 1, 2, 2, 1, 3];
                    var vertexStart = surfacePositions.Count;
                    foreach (var i in order)
                    {
                        // PS1 GT primitives modulate sampled texture by RGB/128. Lit meshes use
                        // normalColors indexed after the vertex indices; FT keeps its first color.
                        // This initial baked colour is replaced by the stage light set once the
                        // stage is bound, while retaining source normal/flat command semantics.
                        var color = PolygonColor(polygon, i, normals);
                        var vertex = vertices[indices[i].GetInt32()];
                        var texUv = new Vector2(uv[i][0].GetSingle() / 256f, uv[i][1].GetSingle() / 256f);
                        var isLit = polygon.GetProperty("lit").GetBoolean();
                        var normal = Vector3.Up;
                        if (isLit)
                        {
                            var normalIndices = polygon.GetProperty("normal_indices");
                            var sourceCorner = polygon.GetProperty("gouraud").GetBoolean() ? i : 0;
                            normal = normals[normalIndices[sourceCorner].GetInt32()];
                        }
                        surface.SetColor(color);
                        surface.SetUV(texUv);
                        surface.AddVertex(vertex);
                        surfacePositions.Add(vertex);
                        surfaceUvs.Add(texUv);
                        surfaceLighting.Add(new LightingVertex(normal, isLit, color));
                    }
                    sourceStates.Add((sourceCommand, vertexStart, count));
                }
                // libgs modulates textured primitives by RGB/128. Godot's StandardMaterial
                // stores vertex COLOR in [0,1], so it cannot retain the source 0x81..0xFF
                // light range. This unshaded shader receives source RGB/255 and restores the
                // missing multiplication by two after interpolation.
                var material = CreatePs1Material(texture.Texture, key.Abr == 0, cullBack: true);
                model._materials.Add(material);
                var instance = new MeshInstance3D { Mesh = surface.Commit(), MaterialOverride = material };
                model._bones[bone].AddChild(instance);
                var litSurface = new LitSurface(instance, bone, [.. surfacePositions], [.. surfaceUvs],
                    [.. surfaceLighting], material, key.Abr);
                model._litSurfaces.Add(litSurface);
                foreach (var state in sourceStates)
                    model._sourcePolygons.Add(new SourcePolygonState(litSurface, bone, state.Command, state.VertexStart,
                        state.Size, key.Texture, key.Abr));
                meshCount++;
            }
        }
        model.BoneCount = model._bones.Count;
        model.MeshCount = meshCount;
        model.RefreshLighting();
        model.UpdateFace(0);
        return model;
    }

    private static Color PolygonColor(JsonElement polygon, int corner, IReadOnlyList<Vector3> normals)
    {
        if (!polygon.GetProperty("lit").GetBoolean())
        {
            var flat = polygon.GetProperty("flat_color");
            return new Color(flat[0].GetSingle() / 255f, flat[1].GetSingle() / 255f, flat[2].GetSingle() / 255f);
        }
        var normalIndices = polygon.GetProperty("normal_indices");
        var sourceCorner = polygon.GetProperty("gouraud").GetBoolean() ? corner : 0;
        var normal = normals[normalIndices[sourceCorner].GetInt32()];
        // Neutral ambient 0x40 plus one source-like directional term. The runtime's stage
        // light binding replaces this deterministic fallback with the decoded LightSet.
        var intensity = Mathf.Clamp(0.5f + Mathf.Max(0f, normal.Dot(new Vector3(-0.35f, 0.8f, 0.45f))) * 0.9f, 0f, 2f);
        return new Color(intensity * 128f / 255f, intensity * 128f / 255f, intensity * 128f / 255f);
    }

    /// <summary>Binds the current GsSetFlatLight/SetBackColor state. Every pose rebuilds only
    /// its vertex colours: texture page, source UVs, flat polygons and Gouraud sharing stay intact.</summary>
    public void SetLighting(BattleLightSet? lights)
    {
        _lightSet = lights;
        RefreshLighting();
    }

    /// <summary>the decoded mesh contract setColor(mode 1): non-lit primitive command colours are replaced.
    /// A null colour restores the normal colorMode 0 path.</summary>
    public void SetModelColor(Color? color)
    {
        _modelColor = color;
        RefreshLighting();
    }

    private static BattleLightSet ReadLightSet(JsonElement value)
    {
        var lights = new List<BattleLight>();
        foreach (var light in value.GetProperty("lights").EnumerateArray())
        {
            var dir = ReadTriple(light.GetProperty("dir"));
            var rgb = ReadTriple(light.GetProperty("rgb"));
            // Same handed conversion as mesh normals: PS1 Y-down/Z-forward -> Godot Y-up/-Z.
            lights.Add(new BattleLight(new Vector3(dir[0], -dir[1], -dir[2]).Normalized(),
                new Color(rgb[0] / 255f, rgb[1] / 255f, rgb[2] / 255f)));
        }
        if (lights.Count != 3) throw new InvalidOperationException("battle stage light set is not three lights");
        var ambient = ReadTriple(value.GetProperty("ambient"));
        return new BattleLightSet(lights, new Color(ambient[0] / 255f, ambient[1] / 255f, ambient[2] / 255f));
    }

    private void RefreshLighting()
    {
        var capturedVertex = false;
        foreach (var surface in _litSurfaces)
        {
            var tool = new SurfaceTool();
            tool.Begin(Mesh.PrimitiveType.Triangles);
            var basis = _bones[surface.Bone].GlobalTransform.Basis;
            for (var index = 0; index < surface.Positions.Length; index++)
            {
                var source = surface.Lighting[index];
                var color = !source.Lit && _modelColor is { } modelColor ? modelColor : source.Flat;
                if (source.Lit && _lightSet is { } set)
                {
                    // gte_CompMatrix(GsLIGHTWSMATRIX, mesh->matrix) then gte_ncs: composing
                    // the normal by this bone's world basis is the Godot equivalent.
                    // gte_CompMatrix feeds the complete bone matrix, including ScaleMatrix's
                    // fixed-point scale, straight to gte_ncs.  Do not renormalise here: that
                    // would erase animated bone scale and brighten/shadow normals unlike PSX.
                    var normal = basis * source.Normal;
                    color = set.Ambient;
                    foreach (var light in set.Lights)
                        color += light.Rgb * Mathf.Max(0f, light.Direction.Dot(normal));
                    color = new Color(Mathf.Min(1f, color.R), Mathf.Min(1f, color.G), Mathf.Min(1f, color.B));
                    if (!capturedVertex)
                    {
                        _lightingVertexSummary = $"fighter={FighterId} bone={surface.Bone} vertex={index} " +
                            $"normal=({normal.X:0.###},{normal.Y:0.###},{normal.Z:0.###}) " +
                            $"color=({color.R:0.###},{color.G:0.###},{color.B:0.###})";
                        capturedVertex = true;
                    }
                }
                tool.SetUV(surface.Uvs[index]);
                tool.SetColor(color);
                tool.AddVertex(surface.Positions[index]);
                surface.Colors[index] = color;
            }
            surface.Instance.Mesh = tool.Commit();
            surface.Instance.MaterialOverride = surface.Material;
        }
    }

    public string LightingVertexSummary() => _lightingVertexSummary;

    /// <summary>
    /// Source primitives in the decoded mesh contract draw order: bones 1..N, then command-stream order.
    /// Quads remain four-corner primitives even though the existing Godot surface expands them
    /// to two triangles. Colours are those calculated by the latest RefreshLighting call.
    /// </summary>
    /// <summary>Transforms one retained source vertex through its live bone hierarchy.</summary>
    public Vector3 WorldPosition(int bone, Vector3 local) => _bones[bone].GlobalTransform * local;

    public IEnumerable<SourcePolygon> EnumeratePolygons()
    {
        foreach (var state in _sourcePolygons.Where(polygon => polygon.Bone != 0).OrderBy(polygon => polygon.Bone).ThenBy(polygon => polygon.Command))
        {
            var offsets = state.Size == 3 ? new[] { 0, 1, 2 } : [0, 1, 2, 5];
            var positions = offsets.Select(offset => state.Surface.Positions[state.VertexStart + offset]).ToArray();
            var uvs = offsets.Select(offset => state.Surface.Uvs[state.VertexStart + offset]).ToArray();
            var colors = offsets.Select(offset => state.Surface.Colors[state.VertexStart + offset]).ToArray();
            // the decoded mesh contract uses zero for opaque and 1..4 for the PSX ABR modes 0..3.
            // The presentation API exposes -1 for opaque, then the actual ABR mode 0..3.
            yield return new SourcePolygon(state.Bone, state.VertexStart, state.Size, state.TextureKey, state.Abr - 1,
                positions, uvs, colors);
        }
    }

    /// <summary>Every mutable 256x256 tpage currently used by this model.</summary>
    public IEnumerable<(int TextureKey, Image Image)> EnumerateTexturePages() =>
        _faceTextures.Select(pair => (pair.Key, pair.Value.Working));

    /// <summary>Raised after the face-texture update contract's VRAM copy changes a decoded texture page.</summary>
    public event Action<int, Image>? FacePageChanged;

    private static Dictionary<int, FaceTexture> LoadTextures(JsonElement textures, string directory)
    {
        var result = new Dictionary<int, FaceTexture>();
        foreach (var texture in textures.EnumerateArray())
        {
            if (texture.GetProperty("kind").GetString() != "ps1-tpage")
                throw new InvalidOperationException("unsupported battle model texture kind");
            var key = texture.GetProperty("key").GetInt32();
            var imagePath = Path.Combine(directory, texture.GetProperty("image").GetString()
                ?? throw new InvalidOperationException("battle model texture image missing"));
            if (!File.Exists(imagePath))
                throw new InvalidOperationException($"battle model texture image missing: {imagePath}");
            var image = Image.LoadFromFile(imagePath);
            if (image is null || image.IsEmpty() || image.GetWidth() != 256 || image.GetHeight() != 256)
                throw new InvalidOperationException($"battle model texture is not a readable 256x256 page: {imagePath}");
            var source = (Image)image.Duplicate();
            var working = (Image)image.Duplicate();
            if (!result.TryAdd(key, new FaceTexture(source, working, ImageTexture.CreateFromImage(working))))
                throw new InvalidOperationException($"duplicate battle model texture key {key}");
        }
        return result;
    }

    /// <summary>the face-texture update contract's DR_MOVE equivalent. Every decoded tpage/CLUT keeps its own mutable
    /// page, copied from an immutable source page, so palette variants use their exact pixels.</summary>
    private void ConfigureFace(JsonElement root, IReadOnlyDictionary<int, FaceTexture> textures)
    {
        _ = textures;
        if (!root.TryGetProperty("face", out var face)) return;
        foreach (var value in face.GetProperty("parts").EnumerateArray())
        {
            var x = value.GetProperty("x").GetInt32(); var y = value.GetProperty("y").GetInt32();
            var w = value.GetProperty("w").GetInt32(); var h = value.GetProperty("h").GetInt32();
            if (w <= 0) continue;
            var frames = value.GetProperty("frames").EnumerateArray()
                .Select(frame => new Vector2I(frame[0].GetInt32(), frame[1].GetInt32())).ToArray();
            if (frames.Length != 3 || x < 0 || y < 0 || x + w > 256 || y + h > 256
                || frames.Any(frame => frame.X < 0 || frame.Y < 0 || frame.X + w > 256 || frame.Y + h > 256))
                throw new InvalidOperationException("battle face rectangle invalid");
            _faceParts.Add(new FacePart(value.GetProperty("index").GetInt32(), x, y, w, h, frames));
        }
    }

    private void UpdateFace(int framesElapsed)
    {
        if (_faceParts.Count == 0) return;
        var motion = Motion?.Motion ?? 1;
        var eyes = motion is >= 4 and <= 11 ? 2 : motion == 2 ? 1 : 0;
        if (eyes == 0)
        {
            eyes = (_blinkTimer >> 1) switch { 1 or 2 or 5 or 6 => 1, 3 or 4 => 2, _ => 0 };
            if (_blinkTimer >> 1 == 0) _blinkTimer = (_faceRandom() & 0x7f) + 60;
            _blinkTimer = Math.Max(0, _blinkTimer - framesElapsed);
        }
        else _blinkTimer = 0;
        _faceTime += framesElapsed;
        var cycle = _faceTime % 18 / 6;
        foreach (var part in _faceParts) CopyFacePart(part, part.Index < 2 ? eyes : cycle);
    }

    private void CopyFacePart(FacePart part, int frame)
    {
        var source = part.Frames[frame];
        foreach (var (key, texture) in _faceTextures)
        {
            texture.Working.BlitRect(texture.Source, new Rect2I(source, new Vector2I(part.W, part.H)), new Vector2I(part.X, part.Y));
            texture.Texture.Update(texture.Working);
            FacePageChanged?.Invoke(key, texture.Working);
        }
    }

    public int FacePartCount => _faceParts.Count;
    public int FaceChangedTexels()
    {
        var changed = 0;
        foreach (var part in _faceParts)
            foreach (var texture in _faceTextures.Values)
                for (var y = part.Y; y < part.Y + part.H; y++)
                    for (var x = part.X; x < part.X + part.W; x++)
                        if (texture.Source.GetPixel(x, y) != texture.Working.GetPixel(x, y)) changed++;
        return changed;
    }
    public string FaceSummary() => _faceParts.Count == 0 ? $"fighter={FighterId} parts=0" :
        $"fighter={FighterId} parts={_faceParts.Count} eyes_rect=({_faceParts[0].X},{_faceParts[0].Y},{_faceParts[0].W},{_faceParts[0].H})";
    public void SetFaceBlinkTimerForProbe(int timer) { _blinkTimer = timer; }
    public void SetFaceRandomForProbe(Func<int> random) { _faceRandom = random; }

    /// <summary>Poses the hierarchy at a source frame. This is the same local construction as
    /// the decoded mesh contract: ZYX rotation, fixed-point scale (/4096), translation, then parent composition
    /// through the Godot hierarchy. A source-negligible scale hides that bone and its mesh.</summary>
    public void Pose(int frame)
    {
        foreach (var pair in _tracks)
        {
            ApplyPose(pair.Key, Sample(pair.Value[0], frame), Sample(pair.Value[1], frame), Sample(pair.Value[2], frame));
        }
        RefreshLighting();
    }

    public void SetMotion(int motion, bool restart) => (Motion ?? throw new InvalidOperationException("motion state unavailable")).SetMotion(motion, restart);

    /// <summary>Advances PAL frames and applies poseBones or blendBone to this hierarchy.</summary>
    public void Step(int framesElapsed)
    {
        var state = Motion ?? throw new InvalidOperationException("motion state unavailable");
        var priorTarget = state.BlendTarget;
        state.Step(framesElapsed);
        if (!state.Blending) { Pose(state.Frame); UpdateFace(framesElapsed); return; }
        if (priorTarget != state.BlendTarget)
            foreach (var pair in _tracks)
            {
                var source = state.BlendSource == 0xffff ? ReadCurrentPose(pair.Key) :
                    (Sample(pair.Value[0], state.BlendSource), Sample(pair.Value[1], state.BlendSource), Sample(pair.Value[2], state.BlendSource));
                _blendPose[pair.Key] = source;
            }
        foreach (var pair in _tracks)
        {
            var from = _blendPose[pair.Key];
            var to = (FirstKeyAtOrAfter(pair.Value[0], state.Frame), FirstKeyAtOrAfter(pair.Value[1], state.Frame), FirstKeyAtOrAfter(pair.Value[2], state.Frame));
            var scale = HasZero(from.Scale) || HasZero(to.Item3) ? to.Item3 : FixedBlend(from.Scale, to.Item3, state.Blend);
            ApplyPose(pair.Key, FixedBlend(from.Position, to.Item1, state.Blend), FixedBlend(from.Rotation, to.Item2, state.Blend), scale);
        }
        RefreshLighting();
        UpdateFace(framesElapsed);
    }

    /// <summary>Bone indices which the source scale threshold currently suppresses.</summary>
    public IReadOnlyList<int> HiddenBones => [.. _bones.Where(pair => !pair.Value.Visible).Select(pair => pair.Key).Order()];

    /// <summary>Whether a particular source bone is visible after the most recent pose.</summary>
    public bool IsBoneVisible(int index) => _bones.TryGetValue(index, out var bone) && bone.Visible;

    /// <summary>Probe-only legacy comparison: restores all bones to identity scale and visible.
    /// Production always calls <see cref="Pose"/> immediately afterwards.</summary>
    public void ShowAllBonesForProbe()
    {
        foreach (var bone in _bones.Values)
        {
            bone.Scale = Vector3.One;
            bone.Visible = true;
        }
    }

    /// <summary>Probe-only visibility toggle for the model's rendered mesh instances.</summary>
    public void SetVisibleForProbe(bool visible)
    {
        Root.Visible = visible;
        foreach (var surface in _litSurfaces)
            surface.Instance.Visible = visible && (!_painterLayerActive || surface.Abr > 1);
    }

    /// <summary>Moves opaque and PSX ABR-0 primitives to BattleModelLayer. Later ABR modes
    /// stay on their existing Godot surfaces until their source blend equations are implemented.</summary>
    public void SetPainterLayerActive(bool active)
    {
        _painterLayerActive = active;
        foreach (var surface in _litSurfaces)
            surface.Instance.Visible = Root.Visible && (!active || surface.Abr > 1);
    }

    /// <summary>Probe-only culling comparison. Production materials are always restored to Back.</summary>
    public void SetCullModeForProbe(BaseMaterial3D.CullModeEnum mode)
    {
        var cullBack = mode == BaseMaterial3D.CullModeEnum.Back;
        foreach (var surface in _litSurfaces)
        {
            var texture = (Texture2D)surface.Material.GetShaderParameter("tex");
            var alphaScissor = (bool)surface.Material.GetShaderParameter("alpha_scissor");
            surface.Material.Shader = CreatePs1Shader(cullBack, opaque: alphaScissor);
            surface.Material.SetShaderParameter("tex", texture);
            surface.Material.SetShaderParameter("alpha_scissor", alphaScissor);
        }
    }

    /// <summary>Whether a surface is drawn in Godot's opaque pass (depth written). Every
    /// non-ABR primitive is opaque on the PS1: only CLUT word 0x0000 is cut out.</summary>
    public static bool IsOpaqueMaterial(ShaderMaterial material) =>
        !material.Shader.Code.Contains("ALPHA =", StringComparison.Ordinal);

    /// <summary>Probe view of every surface material, to assert the opaque/translucent split.</summary>
    public IReadOnlyList<ShaderMaterial> Materials => _materials;

    /// <summary>Compares ApplyPose's converted Godot basis and scale with the explicit
    /// the decoded mesh contract sequence RotMatrixZYX_gte, ScaleMatrix, then C=diag(1,-1,-1).
    /// The values cover the three principal axes and a non-commuting mixed rotation.</summary>
    public float RotationOracleMaxDiff()
    {
        var bone = _bones.Keys.Where(index => index != 0).DefaultIfEmpty(_bones.Keys.First()).First();
        var samples = new[]
        {
            (new Vector3(0x200, 0, 0), new Vector3(4096, 4096, 4096)),
            (new Vector3(0, 0x200, 0), new Vector3(4096, 4096, 4096)),
            (new Vector3(0, 0, 0x200), new Vector3(4096, 4096, 4096)),
            (new Vector3(0x123, 0x345, 0x567), new Vector3(4096, 2048, 8192)),
        };
        var conversion = new Basis(Vector3.Right, Vector3.Down, Vector3.Back);
        var max = 0f;
        foreach (var (rotation, scale) in samples)
        {
            ApplyPose(bone, Vector3.Zero, rotation, scale);
            var radians = rotation * (Mathf.Tau / 4096f);
            var sx = Mathf.Sin(radians.X); var cx = Mathf.Cos(radians.X);
            var sy = Mathf.Sin(radians.Y); var cy = Mathf.Cos(radians.Y);
            var sz = Mathf.Sin(radians.Z); var cz = Mathf.Cos(radians.Z);
            // Explicit columns of mZ*mY*mX, independently of Basis.FromEuler.
            var expectedSource = new Basis(
                new Vector3(cz * cy, sz * cy, -sy),
                new Vector3(cz * sy * sx - sz * cx, sz * sy * sx + cz * cx, cy * sx),
                new Vector3(cz * sy * cx + sz * sx, sz * sy * cx - cz * sx, cy * cx));
            var expected = conversion * expectedSource * conversion;
            max = Math.Max(max, BasisMaxDelta(_bones[bone].Basis.Orthonormalized(), expected));
            max = Math.Max(max, (_bones[bone].Scale - scale / 4096f).Length());
        }
        return max;
    }

    /// <summary>Compares the Godot screen winding of every emitted source triangle to its source
    /// normal facing test. Both predicates use the active bone's world transform.</summary>
    public (int Total, int Agree) WindingAgreement(Camera3D camera)
    {
        var total = 0; var agree = 0;
        foreach (var surface in _litSurfaces)
            for (var i = 0; i + 2 < surface.Positions.Length; i += 3)
            {
                var a = surface.Instance.GlobalTransform * surface.Positions[i];
                var b = surface.Instance.GlobalTransform * surface.Positions[i + 1];
                var c = surface.Instance.GlobalTransform * surface.Positions[i + 2];
                var pa = camera.UnprojectPosition(a); var pb = camera.UnprojectPosition(b); var pc = camera.UnprojectPosition(c);
                var screenFront = (pb.X - pa.X) * (pc.Y - pa.Y) - (pc.X - pa.X) * (pb.Y - pa.Y) > 0f;
                // The PSX has no geometric normal-facing stage. the decoded mesh contract calls gte_nclip on
                // these projected source vertices, so its source face normal is this signed screen
                // area. Lighting normals are deliberately excluded: they are for gte_ncs only.
                var sourceFront = (pb.X - pa.X) * (pc.Y - pa.Y) - (pc.X - pa.X) * (pb.Y - pa.Y) > 0f;
                if (screenFront == sourceFront) agree++;
                total++;
            }
        return (total, agree);
    }

    private static float BasisMaxDelta(Basis a, Basis b) => Math.Max((a.X - b.X).Length(), Math.Max((a.Y - b.Y).Length(), (a.Z - b.Z).Length()));

    private static ShaderMaterial CreatePs1Material(Texture2D texture, bool alphaScissor, bool cullBack)
    {
        var material = new ShaderMaterial { Shader = CreatePs1Shader(cullBack, opaque: alphaScissor) };
        material.SetShaderParameter("tex", texture);
        material.SetShaderParameter("alpha_scissor", alphaScissor);
        return material;
    }

    // A spatial shader that writes ALPHA is moved to Godot's transparent pass,
    // which neither writes depth nor sorts per polygon. Every Kotemon/stage surface was
    // therefore blended in per-object order, and the stage's ABR floor (drawn later) washed
    // over the fighters' feet. Non-ABR primitives are opaque on the PS1 (GPU semi-transparency
    // needs the ABR command bit; texel 0x0000 alone is skipped), so they must never write ALPHA:
    // they discard cut-out texels and keep the depth buffer. Only ABR surfaces stay translucent.
    private static Shader CreatePs1Shader(bool cullBack, bool opaque) => new()
    {
        Code = opaque
            ? $@"shader_type spatial;
render_mode unshaded, depth_draw_opaque, {(cullBack ? "cull_back" : "cull_disabled")};
uniform sampler2D tex : source_color, filter_nearest;
uniform bool alpha_scissor = true;
void fragment() {{
    vec4 sampled = texture(tex, UV);
    if (sampled.a < 0.25) discard;
    ALBEDO = sampled.rgb * COLOR.rgb * 2.0;
}}"
            : $@"shader_type spatial;
render_mode unshaded, {(cullBack ? "cull_back" : "cull_disabled")};
uniform sampler2D tex : source_color, filter_nearest;
uniform bool alpha_scissor = false;
void fragment() {{
    vec4 sampled = texture(tex, UV);
    ALBEDO = sampled.rgb * COLOR.rgb * 2.0;
    ALPHA = sampled.a;
}}"
    };

    /// <summary>The ordered keyframe numbers a motion plays (the non-blend steps of
    /// FIGHTSTG_setMotion), for posing an attack/technique/idle at a real keyframe.</summary>
    public IReadOnlyList<int> MotionKeyframes(int motionIndex)
    {
        var frames = new List<int>();
        if (motionIndex < 0 || motionIndex >= _motions.Count) return frames;
        foreach (var step in _motions[motionIndex])
        {
            var index = step[0];
            var count = step[1];
            var frame = step[2];
            var endFrame = step[3];
            if (index == 0x7FFF || count == 0) break;
            if (endFrame == 0) continue; // a blend step: skipped for the first-pass pose
            for (var i = 0; i < count; i++) frames.Add(frame + i);
        }
        return frames;
    }

    private static int[] ReadTriple(JsonElement element)
    {
        return [element[0].GetInt32(), element[1].GetInt32(), element[2].GetInt32()];
    }

    /// <summary>PS1 libgs/GTE uses Y down and Z forward. Godot uses Y up and looks down -Z.
    /// The two sign changes preserve handedness and face winding.</summary>
    public static Vector3 Ps1Position(Vector3 value) =>
        new Vector3(value.X, -value.Y, -value.Z) / BattleSceneMath.Ps1UnitsPerGodotUnit;

    private static Vector3 Sample(List<Vector4> keys, int frame)
    {
        if (keys.Count == 0) return Vector3.Zero;
        if (frame <= keys[0].W) return new Vector3(keys[0].X, keys[0].Y, keys[0].Z);
        for (var i = 1; i < keys.Count; i++)
        {
            if (frame <= keys[i].W)
            {
                var a = keys[i - 1];
                var b = keys[i];
                if (frame == b.W) return new Vector3(b.X, b.Y, b.Z);
                // FIGHTSTG_poseBone uses a 12-bit GTE factor, then signed integer products.
                // Do not use float Lerp here: visibility crosses the <=48 threshold on this
                // exact fixed-point result.
                var factor = ((frame - (int)a.W) << 12) / ((int)b.W - (int)a.W);
                return new Vector3(
                    (int)a.X + (((int)b.X - (int)a.X) * factor >> 12),
                    (int)a.Y + (((int)b.Y - (int)a.Y) * factor >> 12),
                    (int)a.Z + (((int)b.Z - (int)a.Z) * factor >> 12));
            }
        }
        var last = keys[^1];
        return new Vector3(last.X, last.Y, last.Z);
    }

    private (Vector3 Position, Vector3 Rotation, Vector3 Scale) ReadCurrentPose(int bone) =>
        _blendPose.TryGetValue(bone, out var pose) ? pose : (Vector3.Zero, Vector3.Zero, new Vector3(4096, 4096, 4096));

    private void ApplyPose(int bone, Vector3 positionFixed, Vector3 rotationFixed, Vector3 scaleFixed)
    {
        var sx = (int)scaleFixed.X; var sy = (int)scaleFixed.Y; var sz = (int)scaleFixed.Z;
        var node = _bones[bone];
        node.Position = Ps1Position(positionFixed);
        var sourceBasis = Basis.FromEuler(rotationFixed * (Mathf.Pi / 2048f), EulerOrder.Zyx);
        var conversion = new Basis(Vector3.Right, Vector3.Down, Vector3.Back);
        node.Basis = conversion * sourceBasis * conversion;
        node.Scale = new Vector3(sx, sy, sz) / 4096f;
        node.Visible = (sx | sy | sz) > 48;
        _blendPose[bone] = (positionFixed, rotationFixed, scaleFixed);
    }

    private static Vector3 FirstKeyAtOrAfter(List<Vector4> keys, int frame)
    {
        foreach (var key in keys) if (key.W >= frame) return new Vector3(key.X, key.Y, key.Z);
        var last = keys[^1]; return new Vector3(last.X, last.Y, last.Z);
    }

    private static Vector3 FixedBlend(Vector3 from, Vector3 to, int blend) => new(
        (int)from.X + (((int)to.X - (int)from.X) * blend >> 12),
        (int)from.Y + (((int)to.Y - (int)from.Y) * blend >> 12),
        (int)from.Z + (((int)to.Z - (int)from.Z) * blend >> 12));

    private static bool HasZero(Vector3 value) => value.X == 0 || value.Y == 0 || value.Z == 0;
}
