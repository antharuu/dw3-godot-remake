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

    /// <summary>Initializes a new instance of the <see cref="BattleModel"/> class after every decoded component has been validated and built.</summary>
    /// <param name="root">Scene node that owns the model hierarchy.</param>
    /// <param name="skeleton">Bone hierarchy and animation tracks for the model.</param>
    /// <param name="face">Texture-page animator for facial regions.</param>
    /// <param name="motions">Recovered motion command streams indexed by motion number.</param>
    /// <param name="motion">State machine that advances the selected motion stream.</param>
    /// <param name="meshes">Built mesh surfaces and their retained source metadata.</param>
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

    /// <summary>Gets the optional stage background colour recovered with a fight-stage model.</summary>
    public Color? StageBgColor { get; private set; }

    /// <summary>Gets the root node containing bones and mesh instances for this model.</summary>
    public Node3D Root { get; }

    /// <summary>Gets a value indicating whether the model root is currently visible to the battle viewport.</summary>
    public bool Visible => Root.Visible;

    /// <summary>Gets the number of decoded bones in the hierarchy.</summary>
    public int BoneCount { get; }

    /// <summary>Gets the number of mesh instances emitted from the model geometry.</summary>
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

    /// <summary>Gets the fighter identifier embedded in a fighter model, or zero for a stage.</summary>
    public int FighterId { get; private set; }

    /// <summary>Gets the recovered placement distance in source coordinate units.</summary>
    public int Distance { get; private set; }

    /// <summary>Gets the recovered placement height in source coordinate units.</summary>
    public int Height { get; private set; }

    /// <summary>Gets the motion state machine, when the model contains an animation stream.</summary>
    public BattleMotionMachine? Motion { get; }

    /// <summary>Gets stage directional lights and ambient colour when this model is a battle stage.</summary>
    public BattleLightSet? StageLights { get; private set; }

    /// <summary>Gets the number of independently animated face regions.</summary>
    public int FacePartCount => _face.PartCount;

    /// <summary>Gets bone indices which the source scale threshold currently suppresses.</summary>
    public IReadOnlyList<int> HiddenBones => _skeleton.HiddenBones;

    /// <summary>Gets probe view of every surface material, to assert the opaque/translucent split.</summary>
    public IReadOnlyList<ShaderMaterial> Materials => _materials;

    /// <summary>Loads a battle-model conversion from <paramref name="jsonPath"/>, building the
    /// model hierarchy as a child of <paramref name="parent"/>. The texture images live beside
    /// the JSON. Rejects a missing file, wrong format or a changed motion-1 invariant.</summary>
    /// <param name="parent">Scene node that receives the new model root.</param>
    /// <param name="jsonPath">Path to the extracted model JSON and sibling texture images.</param>
    /// <returns>A fully built model with its initial face texture applied.</returns>
    /// <exception cref="InvalidOperationException">The extracted model contract is missing or invalid.</exception>
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
        var skeleton = new BattleSkeleton(rootNode, bones, BattleModelDocument.ReadTracks(root.GetProperty("tracks")));
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

    /// <summary>Determines whether a surface is drawn in Godot's opaque pass with depth writes. Every
    /// non-ABR primitive is opaque on the PS1: only CLUT word 0x0000 is cut out.</summary>
    /// <param name="material">Material whose blend and cutout state is inspected.</param>
    /// <returns><see langword="true"/> when the material belongs to the opaque pass.</returns>
    public static bool IsOpaqueMaterial(ShaderMaterial material) => BattlePs1Material.IsOpaque(material);

    /// <summary>Converts a source-space position to Godot coordinates. PS1 libgs/GTE uses Y down and Z forward. Godot uses Y up and looks down -Z.
    /// The two sign changes preserve handedness and face winding.</summary>
    /// <param name="value">Position expressed in source fixed coordinate units.</param>
    /// <returns>The position converted to Godot world units.</returns>
    public static Vector3 Ps1Position(Vector3 value) =>
        new Vector3(value.X, -value.Y, -value.Z) / BattleSceneMath.Ps1UnitsPerGodotUnit;

    /// <summary>Captures the currently published motion state and every effective bone pose for a cadence probe checkpoint.</summary>
    /// <param name="sourceFrame">Pinned elapsed source PAL frames at the checkpoint.</param>
    /// <returns>The observable machine publication and skeleton result from the live production model.</returns>
    public BattleMotionCadenceSample CaptureCadenceSample(int sourceFrame)
    {
        var motion = Motion ?? throw new InvalidOperationException("motion state unavailable");
        return new BattleMotionCadenceSample(
            sourceFrame,
            motion.Motion,
            motion.Keyframe,
            motion.Frame,
            motion.Blend,
            motion.BlendTarget,
            motion.BlendSource,
            motion.Blending,
            _skeleton.CaptureCadenceBones());
    }

    /// <summary>Binds the current directional and ambient light state. Every pose rebuilds only
    /// its vertex colours: texture page, source UVs, flat polygons and Gouraud sharing stay intact.</summary>
    /// <param name="lights">Stage lights to apply, or <see langword="null"/> to clear them.</param>
    public void SetLighting(BattleLightSet? lights)
    {
        _lighting.LightSet = lights;
        RefreshLighting();
    }

    /// <summary>Applies the model-colour path for non-lit primitives. A non-null colour replaces their command colours.
    /// A null colour restores the normal colorMode 0 path.</summary>
    /// <param name="color">Replacement colour, or <see langword="null"/> to restore per-primitive colours.</param>
    public void SetModelColor(Color? color)
    {
        _lighting.ModelColor = color;
        RefreshLighting();
    }

    /// <summary>Gets a compact diagnostic description of the latest lit-vertex calculation.</summary>
    /// <returns>A stable summary of lighting inputs and resulting vertex colours.</returns>
    public string LightingVertexSummary() => _lighting.VertexSummary;

    /// <summary>Transforms one retained source vertex through its live bone hierarchy.</summary>
    /// <param name="bone">Zero-based bone index that owns the vertex.</param>
    /// <param name="local">Vertex position in the bone's local source space.</param>
    /// <returns>The vertex position in the model's current world space.</returns>
    public Vector3 WorldPosition(int bone, Vector3 local) => Root.GlobalTransform * _skeleton.WorldPosition(bone, local);

    /// <summary>
    /// Source primitives in the decoded mesh contract draw order: bones 1..N, then command-stream order.
    /// Quads remain four-corner primitives even though the existing Godot surface expands them
    /// to two triangles. Colours are those calculated by the latest RefreshLighting call.
    /// </summary>
    /// <returns>Source-order primitives retaining their original corner grouping and current colours.</returns>
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

    /// <summary>Enumerates every mutable 256 by 256 texture page currently used by this model.</summary>
    /// <returns>Texture-page keys paired with their writable images.</returns>
    public IEnumerable<(int TextureKey, Image Image)> EnumerateTexturePages() => _face.Pages;

    /// <summary>Gets the live GPU texture page used by an ordered raster command.</summary>
    /// <param name="textureKey">Decoded tpage/CLUT page key.</param>
    /// <returns>The mutable texture synchronized with face animation updates.</returns>
    /// <exception cref="InvalidOperationException">The model has no page with <paramref name="textureKey"/>.</exception>
    public Texture2D TextureFor(int textureKey) =>
        _face.TextureFor(textureKey);

    /// <summary>Gets the number of face texture texels changed since the model was loaded.</summary>
    /// <returns>The accumulated count of modified face-page texels.</returns>
    public int FaceChangedTexels() => _face.ChangedTexels();

    /// <summary>Gets a diagnostic summary of the current face-animation state.</summary>
    /// <returns>A stable description including this model's fighter identifier.</returns>
    public string FaceSummary() => _face.Summary(FighterId);

    /// <summary>Sets the face blink countdown for a deterministic visual probe.</summary>
    /// <param name="timer">Countdown expressed in source animation frames.</param>
    public void SetFaceBlinkTimerForProbe(int timer) => _face.SetBlinkTimer(timer);

    /// <summary>Supplies deterministic random values to the face animator during a visual probe.</summary>
    /// <param name="random">Callback that provides the next source-range random value.</param>
    public void SetFaceRandomForProbe(Func<int> random) => _face.SetRandom(random);

    /// <summary>
    /// Synchronizes this independent render copy's pose and face to the active fighter without
    /// sharing mutable image pages or scene nodes between viewports.
    /// </summary>
    /// <param name="source">The active fighter whose face frame is authoritative.</param>
    public void SynchronizePresentationFrom(BattleModel source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _skeleton.SynchronizeFrom(source._skeleton);
        SynchronizeSurfaceTransforms();
        RefreshLighting();
        _face.SynchronizeFrom(source._face);
    }

    /// <summary>Poses the hierarchy at a source frame. This is the same local construction as
    /// the decoded mesh contract: ZYX rotation, fixed-point scale (/4096), translation, then parent composition
    /// through the Godot hierarchy. A source-negligible scale hides that bone and its mesh.</summary>
    /// <param name="frame">Source animation frame to sample.</param>
    public void Pose(int frame)
    {
        _skeleton.Pose(frame);
        SynchronizeSurfaceTransforms();
        RefreshLighting();
    }

    /// <summary>Selects a one-based source motion in the model's motion state machine.</summary>
    /// <param name="motion">One-based recovered motion number.</param>
    /// <param name="restart">Whether to reset the selected motion to its first step.</param>
    /// <exception cref="InvalidOperationException">The model has no motion state machine.</exception>
    public void SetMotion(int motion, bool restart) => (Motion ?? throw new InvalidOperationException("motion state unavailable")).SetMotion(motion, restart);

    /// <summary>Advances PAL frames and applies poseBones or blendBone to this hierarchy.</summary>
    /// <param name="framesElapsed">Number of PAL animation frames to advance.</param>
    /// <exception cref="InvalidOperationException">The model has no motion state machine.</exception>
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
        SynchronizeSurfaceTransforms();
        RefreshLighting();
        UpdateFace(framesElapsed);
    }

    /// <summary>Whether a particular source bone is visible after the most recent pose.</summary>
    /// <param name="index">Zero-based bone index to inspect.</param>
    /// <returns><see langword="true"/> when the bone survived the latest scale threshold.</returns>
    public bool IsBoneVisible(int index) => _skeleton.IsVisible(index);

    /// <summary>Reports whether a detached legacy surface owned by one bone remains visible in the scene tree.</summary>
    /// <param name="bone">Source bone index that owns the surface.</param>
    /// <returns>True when at least one locally visible legacy surface is composable by Godot.</returns>
    public bool HasVisibleLegacySurface(int bone) =>
        _litSurfaces.Any(surface => surface.Bone == bone && surface.Instance.IsVisibleInTree());

    /// <summary>Counts source polygons that the common ordering path can draw from one locally visible bone.</summary>
    /// <param name="bone">Source bone index that owns the polygons.</param>
    /// <returns>The number of locally drawable source primitives.</returns>
    public int DrawablePolygonCount(int bone) =>
        IsBoneVisible(bone) ? EnumeratePolygons().Count(polygon => polygon.Bone == bone) : 0;

    /// <summary>Probe-only legacy comparison: restores all bones to identity scale and visible.
    /// Production always calls <see cref="Pose"/> immediately afterwards.</summary>
    public void ShowAllBonesForProbe() => _skeleton.ShowAll();

    /// <summary>Probe-only visibility toggle for the model's rendered mesh instances.</summary>
    /// <param name="visible">Whether the model surfaces should be visible in the probe capture.</param>
    public void SetVisibleForProbe(bool visible)
    {
        Root.Visible = visible;
        foreach (var surface in _litSurfaces)
        {
            ApplyLegacySurfaceVisibility(surface, visible);
        }
    }

    /// <summary>Moves all observed source primitives to BattleModelLayer's ordered canvas command stream.</summary>
    /// <param name="active">Whether the ordering layer owns the model's source primitives.</param>
    public void SetPainterLayerActive(bool active)
    {
        _painterLayerActive = active;
        foreach (var surface in _litSurfaces)
        {
            ApplyLegacySurfaceVisibility(surface, Root.Visible);
        }
    }

    /// <summary>Probe-only culling comparison. Production materials are always restored to Back.</summary>
    /// <param name="mode">Cull mode to apply to every model surface.</param>
    public void SetCullModeForProbe(BaseMaterial3D.CullModeEnum mode)
    {
        var cullBack = mode == BaseMaterial3D.CullModeEnum.Back;
        foreach (var material in _litSurfaces.Select(surface => surface.Material))
        {
            var texture = (Texture2D)material.GetShaderParameter("tex");
            var alphaScissor = (bool)material.GetShaderParameter("alpha_scissor");
            material.Shader = BattlePs1Material.CreateShader(cullBack, opaque: alphaScissor);
            material.SetShaderParameter("tex", texture);
            material.SetShaderParameter("alpha_scissor", alphaScissor);
        }
    }

    /// <summary>Compares ApplyPose's converted Godot basis and scale with the explicit
    /// the decoded mesh contract sequence RotMatrixZYX_gte, ScaleMatrix, then C=diag(1,-1,-1).
    /// The values cover the three principal axes and a non-commuting mixed rotation.</summary>
    /// <returns>The greatest component difference between the two transform constructions.</returns>
    public float RotationOracleMaxDiff() => _skeleton.RotationOracleMaxDiff();

    /// <summary>Compares the Godot screen winding of every emitted source triangle to its source
    /// normal facing test. Both predicates use the active bone's world transform.</summary>
    /// <param name="camera">Active camera used to project surface triangles.</param>
    /// <returns>Total inspected triangles and how many have matching winding predicates.</returns>
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
    /// <param name="motionIndex">Zero-based index in the recovered motion list.</param>
    /// <returns>Keyframe numbers for non-blend steps, or an empty list for an invalid index.</returns>
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

    /// <summary>Updates top-level legacy surfaces from the retained source-style bone matrices.</summary>
    private void SynchronizeSurfaceTransforms()
    {
        foreach (var surface in _litSurfaces)
        {
            surface.Instance.GlobalTransform = Root.GlobalTransform * _skeleton.WorldTransform(surface.Bone);
            ApplyLegacySurfaceVisibility(surface, Root.Visible);
        }
    }

    /// <summary>Applies source-local bone visibility to a detached legacy surface.</summary>
    /// <param name="surface">Legacy surface whose owning bone determines its local visibility.</param>
    /// <param name="rootVisible">Whether the owning model root is locally visible.</param>
    private void ApplyLegacySurfaceVisibility(BattleLitSurface surface, bool rootVisible) =>
        surface.Instance.Visible = rootVisible && _skeleton.IsVisible(surface.Bone) && !_painterLayerActive;

    /// <summary>Recalculates lit vertex colours after a pose or lighting-state change.</summary>
    private void RefreshLighting() => _lighting.Refresh(_litSurfaces, _skeleton, FighterId);

    /// <summary>Advances face animation with the currently selected motion number.</summary>
    /// <param name="framesElapsed">Number of PAL animation frames to advance.</param>
    private void UpdateFace(int framesElapsed) => _face.Update(framesElapsed, Motion?.Motion ?? 1);

    /// <summary>One decoded fighter camera view.</summary>
    public readonly record struct BattleCameraView(int[] CamPos, int[] CamRef, int Proj);

    /// <summary>One directional light from a recovered battle-stage lighting set.</summary>
    /// <param name="Direction">Light direction in source-derived model coordinates.</param>
    /// <param name="Rgb">Linear RGB intensity applied by the vertex-lighting pass.</param>
    public readonly record struct BattleLight(Vector3 Direction, Color Rgb);

    /// <summary>Directional lights and ambient contribution used to shade a battle scene.</summary>
    /// <param name="Lights">Ordered directional light records from the stage conversion.</param>
    /// <param name="Ambient">RGB ambient contribution added to every lit vertex.</param>
    public readonly record struct BattleLightSet(IReadOnlyList<BattleLight> Lights, Color Ambient);

    /// <summary>One source primitive in the decoded mesh contract order, retaining its un-split quad and the
    /// colours computed by the most recent <see cref="RefreshLighting"/>.</summary>
    public readonly record struct SourcePolygon(int Bone, int VertexStart, int Size, int TextureKey, int Abr, Vector3[] Positions, Vector2[] Uvs, Color[] Colors);
}
