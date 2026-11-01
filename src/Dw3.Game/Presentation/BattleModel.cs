using System.Text.Json;
using Godot;

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
    private readonly BattleSkeleton _skeleton;
    private readonly BattleFaceAnimator _face;
    private readonly List<List<List<int>>> _motions;
    private readonly List<ShaderMaterial> _materials;
    private readonly List<BattleLitSurface> _litSurfaces;
    private readonly List<BattleSourcePolygonState> _sourcePolygons;
    private readonly BattleModelLighting _lighting = new();
    private bool _painterLayerActive;

    private BattleModel(Node3D root, BattleSkeleton skeleton, BattleFaceAnimator face, List<List<List<int>>> motions, BattleMotionMachine motion, BattleMeshBuilder meshes)
    {
        Root = root;
        _skeleton = skeleton;
        _face = face;
        _motions = motions;
        _materials = meshes.Materials;
        _litSurfaces = meshes.Surfaces;
        _sourcePolygons = meshes.SourcePolygons;
        Motion = motion;
        BoneCount = skeleton.Count;
        MeshCount = meshes.MeshCount;
        UntexturedPolygonCount = meshes.UntexturedPolygonCount;
    }

    /// <summary>Raised after the face-texture update contract's VRAM copy changes a decoded texture page.</summary>
    public event Action<int, Image>? FacePageChanged
    {
        add => _face.PageChanged += value;
        remove => _face.PageChanged -= value;
    }

    public Color? StageBgColor { get; private set; }

    public Node3D Root { get; }

    public bool Visible => Root.Visible;

    public int BoneCount { get; }

    public int MeshCount { get; }

    /// <summary>Gets polygons whose source 0x9 command disabled new UV loading but which still
    /// render through the residual POLY_FT/GT texture state.</summary>
    public int UntexturedPolygonCount { get; }

    /// <summary>Gets the model's motions, index 0 = motion 1 (the idle). Each is a list of steps
    /// <c>[index, count, frame, endFrame]</c> ending on the 0x7FFF terminator.</summary>
    public IReadOnlyList<IReadOnlyList<IReadOnlyList<int>>> Motions => _motions;

    /// <summary>Gets the fighter's camera views (the decoded fighter-camera record contract / the decoded battle-camera record contract), in PS1
    /// integer units: <c>[x, y, z]</c> position, reference and a projection distance. Empty
    /// for a fight stage (which has no fighter camera).</summary>
    public IReadOnlyList<BattleCameraView> Cameras { get; private set; } = [];

    public int FighterId { get; private set; }

    public int Distance { get; private set; }

    public int Height { get; private set; }

    public BattleMotionMachine? Motion { get; }

    public BattleLightSet? StageLights { get; private set; }

    public int FacePartCount => _face.PartCount;

    /// <summary>Gets bone indices which the source scale threshold currently suppresses.</summary>
    public IReadOnlyList<int> HiddenBones => _skeleton.HiddenBones;

    /// <summary>Gets probe view of every surface material, to assert the opaque/translucent split.</summary>
    public IReadOnlyList<ShaderMaterial> Materials => _materials;

    /// <summary>Loads a battle-model conversion from <paramref name="jsonPath"/>, building the
    /// model hierarchy as a child of <paramref name="parent"/>. The texture images live beside
    /// the JSON. Rejects a missing file, wrong format or a changed motion-1 invariant.</summary>
    public static BattleModel Load(Node3D parent, string jsonPath)
    {
        if (!File.Exists(jsonPath))
        {
            throw new InvalidOperationException($"battle model JSON missing: {jsonPath}");
        }

        using var document = JsonDocument.Parse(File.ReadAllText(jsonPath));
        var root = document.RootElement;
        BattleModelDocument.Validate(root);

        var rootNode = new Node3D { Name = "BattleModel" };
        parent.AddChild(rootNode);
        var stageLights = BattleModelDocument.ReadStageLights(root);
        var stageBgColor = BattleModelDocument.ReadBackgroundColor(root);
        var fighter = BattleModelDocument.ReadFighter(root);
        var bones = BattleSkeleton.BuildBones(rootNode, root.GetProperty("bones"));
        var skeleton = new BattleSkeleton(bones, BattleModelDocument.ReadTracks(root.GetProperty("tracks")));
        var textures = BattleTexturePages.Load(root.GetProperty("textures"), Path.GetDirectoryName(jsonPath) ?? string.Empty);
        var face = new BattleFaceAnimator(textures);
        face.Configure(root);
        var motions = BattleModelDocument.ReadMotions(root.GetProperty("motions"));
        var motion = new BattleMotionMachine(motions);
        var cameras = BattleModelDocument.ReadCameras(root);
        var meshes = new BattleMeshBuilder(skeleton, textures);
        meshes.Build(root.GetProperty("meshes"));

        var model = new BattleModel(rootNode, skeleton, face, motions, motion, meshes)
        {
            StageLights = stageLights,
            StageBgColor = stageBgColor,
            FighterId = fighter?.FighterId ?? 0,
            Distance = fighter?.Distance ?? 0,
            Height = fighter?.Height ?? 0,
            Cameras = cameras ?? [],
        };
        model.RefreshLighting();
        model.UpdateFace(0);
        return model;
    }

    /// <summary>Whether a surface is drawn in Godot's opaque pass (depth written). Every
    /// non-ABR primitive is opaque on the PS1: only CLUT word 0x0000 is cut out.</summary>
    public static bool IsOpaqueMaterial(ShaderMaterial material) => BattlePs1Material.IsOpaque(material);

    /// <summary>PS1 libgs/GTE uses Y down and Z forward. Godot uses Y up and looks down -Z.
    /// The two sign changes preserve handedness and face winding.</summary>
    public static Vector3 Ps1Position(Vector3 value) =>
        new Vector3(value.X, -value.Y, -value.Z) / BattleSceneMath.Ps1UnitsPerGodotUnit;

    /// <summary>Binds the current GsSetFlatLight/SetBackColor state. Every pose rebuilds only
    /// its vertex colours: texture page, source UVs, flat polygons and Gouraud sharing stay intact.</summary>
    public void SetLighting(BattleLightSet? lights)
    {
        _lighting.LightSet = lights;
        RefreshLighting();
    }

    /// <summary>the decoded mesh contract setColor(mode 1): non-lit primitive command colours are replaced.
    /// A null colour restores the normal colorMode 0 path.</summary>
    public void SetModelColor(Color? color)
    {
        _lighting.ModelColor = color;
        RefreshLighting();
    }

    public string LightingVertexSummary() => _lighting.VertexSummary;

    /// <summary>Transforms one retained source vertex through its live bone hierarchy.</summary>
    public Vector3 WorldPosition(int bone, Vector3 local) => _skeleton.WorldPosition(bone, local);

    /// <summary>
    /// Source primitives in the decoded mesh contract draw order: bones 1..N, then command-stream order.
    /// Quads remain four-corner primitives even though the existing Godot surface expands them
    /// to two triangles. Colours are those calculated by the latest RefreshLighting call.
    /// </summary>
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
            yield return new SourcePolygon(state.Bone, state.VertexStart, state.Size, state.TextureKey, state.Abr - 1, positions, uvs, colors);
        }
    }

    /// <summary>Every mutable 256x256 tpage currently used by this model.</summary>
    public IEnumerable<(int TextureKey, Image Image)> EnumerateTexturePages() => _face.Pages;

    public int FaceChangedTexels() => _face.ChangedTexels();

    public string FaceSummary() => _face.Summary(FighterId);

    public void SetFaceBlinkTimerForProbe(int timer) => _face.SetBlinkTimer(timer);

    public void SetFaceRandomForProbe(Func<int> random) => _face.SetRandom(random);

    /// <summary>Poses the hierarchy at a source frame. This is the same local construction as
    /// the decoded mesh contract: ZYX rotation, fixed-point scale (/4096), translation, then parent composition
    /// through the Godot hierarchy. A source-negligible scale hides that bone and its mesh.</summary>
    public void Pose(int frame)
    {
        _skeleton.Pose(frame);
        RefreshLighting();
    }

    public void SetMotion(int motion, bool restart) => (Motion ?? throw new InvalidOperationException("motion state unavailable")).SetMotion(motion, restart);

    /// <summary>Advances PAL frames and applies poseBones or blendBone to this hierarchy.</summary>
    public void Step(int framesElapsed)
    {
        var state = Motion ?? throw new InvalidOperationException("motion state unavailable");
        var priorTarget = state.BlendTarget;
        state.Step(framesElapsed);
        if (!state.Blending)
        {
            Pose(state.Frame);
            UpdateFace(framesElapsed);
            return;
        }

        _skeleton.Blend(state, priorTarget != state.BlendTarget);
        RefreshLighting();
        UpdateFace(framesElapsed);
    }

    /// <summary>Whether a particular source bone is visible after the most recent pose.</summary>
    public bool IsBoneVisible(int index) => _skeleton.IsVisible(index);

    /// <summary>Probe-only legacy comparison: restores all bones to identity scale and visible.
    /// Production always calls <see cref="Pose"/> immediately afterwards.</summary>
    public void ShowAllBonesForProbe() => _skeleton.ShowAll();

    /// <summary>Probe-only visibility toggle for the model's rendered mesh instances.</summary>
    public void SetVisibleForProbe(bool visible)
    {
        Root.Visible = visible;
        foreach (var surface in _litSurfaces)
        {
            surface.Instance.Visible = visible && (!_painterLayerActive || surface.Abr > 1);
        }
    }

    /// <summary>Moves opaque and PSX ABR-0 primitives to BattleModelLayer. Later ABR modes
    /// stay on their existing Godot surfaces until their source blend equations are implemented.</summary>
    public void SetPainterLayerActive(bool active)
    {
        _painterLayerActive = active;
        foreach (var surface in _litSurfaces)
        {
            surface.Instance.Visible = Root.Visible && (!active || surface.Abr > 1);
        }
    }

    /// <summary>Probe-only culling comparison. Production materials are always restored to Back.</summary>
    public void SetCullModeForProbe(BaseMaterial3D.CullModeEnum mode)
    {
        var cullBack = mode == BaseMaterial3D.CullModeEnum.Back;
        foreach (var surface in _litSurfaces)
        {
            var texture = (Texture2D)surface.Material.GetShaderParameter("tex");
            var alphaScissor = (bool)surface.Material.GetShaderParameter("alpha_scissor");
            surface.Material.Shader = BattlePs1Material.CreateShader(cullBack, opaque: alphaScissor);
            surface.Material.SetShaderParameter("tex", texture);
            surface.Material.SetShaderParameter("alpha_scissor", alphaScissor);
        }
    }

    /// <summary>Compares ApplyPose's converted Godot basis and scale with the explicit
    /// the decoded mesh contract sequence RotMatrixZYX_gte, ScaleMatrix, then C=diag(1,-1,-1).
    /// The values cover the three principal axes and a non-commuting mixed rotation.</summary>
    public float RotationOracleMaxDiff() => _skeleton.RotationOracleMaxDiff();

    /// <summary>Compares the Godot screen winding of every emitted source triangle to its source
    /// normal facing test. Both predicates use the active bone's world transform.</summary>
    public (int Total, int Agree) WindingAgreement(Camera3D camera)
    {
        var total = 0;
        var agree = 0;
        foreach (var surface in _litSurfaces)
        {
            for (var i = 0; i + 2 < surface.Positions.Length; i += 3)
            {
                var pa = camera.UnprojectPosition(surface.Instance.GlobalTransform * surface.Positions[i]);
                var pb = camera.UnprojectPosition(surface.Instance.GlobalTransform * surface.Positions[i + 1]);
                var pc = camera.UnprojectPosition(surface.Instance.GlobalTransform * surface.Positions[i + 2]);
                var screenFront = ((pb.X - pa.X) * (pc.Y - pa.Y)) - ((pc.X - pa.X) * (pb.Y - pa.Y)) > 0f;

                // The PSX has no geometric normal-facing stage. the decoded mesh contract calls gte_nclip on
                // these projected source vertices, so its source face normal is this signed screen
                // area. Lighting normals are deliberately excluded: they are for gte_ncs only.
                var sourceFront = ((pb.X - pa.X) * (pc.Y - pa.Y)) - ((pc.X - pa.X) * (pb.Y - pa.Y)) > 0f;
                agree += screenFront == sourceFront ? 1 : 0;
                total++;
            }
        }

        return (total, agree);
    }

    /// <summary>The ordered keyframe numbers a motion plays (the non-blend steps of
    /// FIGHTSTG_setMotion), for posing an attack/technique/idle at a real keyframe.</summary>
    public IReadOnlyList<int> MotionKeyframes(int motionIndex)
    {
        var frames = new List<int>();
        if (motionIndex < 0 || motionIndex >= _motions.Count)
        {
            return frames;
        }

        foreach (var step in _motions[motionIndex])
        {
            var index = step[0];
            var count = step[1];
            if (index == 0x7FFF || count == 0)
            {
                break;
            }

            // A blend step (endFrame 0) is skipped for the first-pass pose.
            if (step[3] != 0)
            {
                for (var i = 0; i < count; i++)
                {
                    frames.Add(step[2] + i);
                }
            }
        }

        return frames;
    }

    private void RefreshLighting() => _lighting.Refresh(_litSurfaces, _skeleton, FighterId);

    private void UpdateFace(int framesElapsed) => _face.Update(framesElapsed, Motion?.Motion ?? 1);

    /// <summary>One decoded fighter camera view.</summary>
    public readonly record struct BattleCameraView(int[] CamPos, int[] CamRef, int Proj);

    public readonly record struct BattleLight(Vector3 Direction, Color Rgb);

    public readonly record struct BattleLightSet(IReadOnlyList<BattleLight> Lights, Color Ambient);

    /// <summary>One source primitive in the decoded mesh contract order, retaining its un-split quad and the
    /// colours computed by the most recent <see cref="RefreshLighting"/>.</summary>
    public readonly record struct SourcePolygon(int Bone, int VertexStart, int Size, int TextureKey, int Abr, Vector3[] Positions, Vector2[] Uvs, Color[] Colors);
}
