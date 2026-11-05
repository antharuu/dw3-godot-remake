using System.Text.Json;
using Godot;

namespace Dw3Production.Presentation;

/// <summary>
/// The bone hierarchy of a battle model and its key tracks. It poses each bone as the PS1 does:
/// ZYX Euler rotation (4096 units per turn), fixed-point scale (/4096) and translation, composed
/// through the Godot node hierarchy, then converted from PS1 axes (Y down, Z forward) by
/// C = diag(1, -1, -1) on both sides of the source basis.
/// </summary>
internal sealed class BattleSkeleton
{
    /// <summary>A bone whose largest fixed-point scale component is at most this value is hidden.</summary>
    private const int HiddenScaleThreshold = 48;

    private static readonly Basis Ps1ToGodot = new(Vector3.Right, Vector3.Down, Vector3.Forward);
    private static readonly Vector3 IdentityScale = new(4096, 4096, 4096);

    private readonly Dictionary<int, Node3D> _bones;
    private readonly Dictionary<int, List<Vector4>[]> _tracks;
    private readonly BattleSkeletonTransformCache _transforms;
    private readonly BattleBlendState _blendState = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="BattleSkeleton"/> class.
    /// </summary>
    /// <param name="root">Model root that owns detached legacy mesh surfaces.</param>
    /// <param name="bones">Bone nodes by source index, already parented.</param>
    /// <param name="tracks">Position, rotation and scale channels by bone index.</param>
    public BattleSkeleton(Node3D root, Dictionary<int, Node3D> bones, Dictionary<int, List<Vector4>[]> tracks)
    {
        RenderRoot = root;
        _bones = bones;
        _tracks = tracks;
        _transforms = new BattleSkeletonTransformCache(BuildParents(bones));
    }

    /// <summary>Gets the number of bones.</summary>
    public int Count => _bones.Count;

    /// <summary>Gets the model root that owns independently visible legacy mesh surfaces.</summary>
    public Node3D RenderRoot { get; }

    /// <summary>Gets the indices of bones the scale threshold currently hides, in ascending order.</summary>
    public IReadOnlyList<int> HiddenBones => [.. _bones.Where(pair => !pair.Value.Visible).Select(pair => pair.Key).Order()];

    /// <summary>Creates one node per bone record under <paramref name="root"/>.</summary>
    /// <param name="root">Model root that receives the parentless bones.</param>
    /// <param name="bones">The model JSON <c>bones</c> array; a parent always precedes its children.</param>
    /// <returns>The bone nodes by source index.</returns>
    public static Dictionary<int, Node3D> BuildBones(Node3D root, JsonElement bones)
    {
        var result = new Dictionary<int, Node3D>();
        foreach (var bone in bones.EnumerateArray())
        {
            var index = bone.GetProperty("index").GetInt32();
            var node = new Node3D { Name = $"Bone{index}" };
            result[index] = node;
            var parent = bone.GetProperty("parent");
            var parentNode = parent.ValueKind == JsonValueKind.Null ? root : result[parent.GetInt32()];
            parentNode.AddChild(node);
        }

        return result;
    }

    /// <summary>Gets a bone node.</summary>
    /// <param name="index">Source bone index.</param>
    /// <returns>The node.</returns>
    public Node3D Bone(int index) => _bones[index];

    /// <summary>Tells whether a bone survived the scale threshold on the latest pose.</summary>
    /// <param name="index">Source bone index.</param>
    /// <returns>False for an unknown or hidden bone.</returns>
    public bool IsVisible(int index) => _bones.TryGetValue(index, out var bone) && bone.Visible;

    /// <summary>Transforms a bone-local point through the retained source-style hierarchy.</summary>
    /// <param name="bone">Source bone index.</param>
    /// <param name="local">Point in the bone's local Godot units.</param>
    /// <returns>The world-space point in model-local presentation coordinates.</returns>
    public Vector3 WorldPosition(int bone, Vector3 local) => _transforms.World(bone) * local;

    /// <summary>Gets a bone's retained transform in model-local presentation space.</summary>
    /// <param name="bone">Source bone index.</param>
    /// <returns>The transform from the most recent visible pose.</returns>
    public Transform3D WorldTransform(int bone) => _transforms.World(bone);

    /// <summary>Captures every effective source-bone transform for the end-to-end cadence probe.</summary>
    /// <returns>Source-indexed world transforms after pose or blend application.</returns>
    public IReadOnlyList<BattleMotionCadenceBone> CaptureCadenceBones() =>
    [
        .. _bones.Keys.Order().Select(index =>
        {
            var transform = _transforms.World(index);
            var basis = transform.Basis;
            var origin = transform.Origin;
            return new BattleMotionCadenceBone(
                index,
                IsVisible(index),
                origin.X,
                origin.Y,
                origin.Z,
                basis.X.X,
                basis.X.Y,
                basis.X.Z,
                basis.Y.X,
                basis.Y.Y,
                basis.Y.Z,
                basis.Z.X,
                basis.Z.Y,
                basis.Z.Z);
        }),
    ];

    /// <summary>Poses every tracked bone at one source frame.</summary>
    /// <param name="frame">Source animation frame.</param>
    public void Pose(int frame)
    {
        foreach (var pair in _tracks)
        {
            ApplyPose(pair.Key, BattleKeyTrack.Sample(pair.Value[0], frame), BattleKeyTrack.Sample(pair.Value[1], frame), BattleKeyTrack.Sample(pair.Value[2], frame));
        }
    }

    /// <summary>Poses every tracked bone on a blend step of the motion machine.</summary>
    /// <param name="state">Motion machine on a blending entry.</param>
    /// <param name="targetChanged">True when the blend target changed this step, which re-captures
    /// the blend start pose: the live pose for source 0xFFFF, otherwise the source frame.</param>
    public void Blend(BattleMotionMachine state, bool targetChanged)
    {
        if (targetChanged)
        {
            CaptureBlendSource(state.BlendSource);
        }

        foreach (var pair in _tracks)
        {
            var from = _blendState.Origin(pair.Key);
            var position = BattleKeyTrack.FirstKeyAtOrAfter(pair.Value[0], state.Frame);
            var rotation = BattleKeyTrack.FirstKeyAtOrAfter(pair.Value[1], state.Frame);
            var scale = BattleKeyTrack.FirstKeyAtOrAfter(pair.Value[2], state.Frame);
            var blendedScale = BlendScale(from.Scale, scale, state.Blend);
            ApplyPose(pair.Key, BattleKeyTrack.FixedBlend(from.Position, position, state.Blend), BattleKeyTrack.FixedBlend(from.Rotation, rotation, state.Blend), blendedScale);
        }
    }

    /// <summary>Copies the effective pose of an identical source skeleton, including hidden-bone transforms.</summary>
    /// <param name="source">Skeleton currently displayed by the active fighter.</param>
    public void SynchronizeFrom(BattleSkeleton source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!_bones.Keys.Order().SequenceEqual(source._bones.Keys.Order()))
        {
            throw new InvalidOperationException("battle skeleton bones do not match");
        }

        foreach (var bone in _bones.Keys)
        {
            var target = _bones[bone];
            var origin = source._bones[bone];
            target.Position = origin.Position;
            target.Basis = origin.Basis;
            target.Scale = origin.Scale;
            target.Visible = origin.Visible;
        }

        _transforms.CopyFrom(source._transforms);
    }

    /// <summary>Restores every bone to identity scale and visible, for a legacy probe comparison.</summary>
    public void ShowAll()
    {
        foreach (var bone in _bones.Values)
        {
            bone.Scale = Vector3.One;
            bone.Visible = true;
        }
    }

    /// <summary>
    /// Compares the posed basis and scale with an explicit PS1 matrix product mZ*mY*mX, scaled and
    /// converted by C = diag(1, -1, -1), over the three principal axes and one mixed rotation.
    /// </summary>
    /// <returns>The largest column or scale difference.</returns>
    public float RotationOracleMaxDiff()
    {
        var bone = _bones.Keys.Where(index => index != 0).DefaultIfEmpty(_bones.Keys.First()).First();
        var samples = new[]
        {
            (new Vector3(0x200, 0, 0), IdentityScale),
            (new Vector3(0, 0x200, 0), IdentityScale),
            (new Vector3(0, 0, 0x200), IdentityScale),
            (new Vector3(0x123, 0x345, 0x567), new Vector3(4096, 2048, 8192)),
        };
        var max = 0f;
        foreach (var (rotation, scale) in samples)
        {
            ApplyPose(bone, Vector3.Zero, rotation, scale);
            var expected = Ps1ToGodot * ExplicitZyx(rotation * (Mathf.Tau / 4096f)) * Ps1ToGodot;
            max = Math.Max(max, BasisMaxDelta(_bones[bone].Basis.Orthonormalized(), expected));
            max = Math.Max(max, (_bones[bone].Scale - (scale / 4096f)).Length());
        }

        return max;
    }

    /// <summary>Selects the source-compatible scale for one blend step.</summary>
    /// <param name="from">The captured start scale.</param>
    /// <param name="target">The target key scale.</param>
    /// <param name="weight">The 12-bit blend weight.</param>
    /// <returns>The target immediately if either endpoint has a zero component, otherwise their fixed blend.</returns>
    internal static Vector3 BlendScale(Vector3 from, Vector3 target, int weight) =>
        BattleKeyTrack.HasZero(from) || BattleKeyTrack.HasZero(target) ? target : BattleKeyTrack.FixedBlend(from, target, weight);

    /// <summary>Builds a basis from ZYX Euler angles without using the engine conversion.</summary>
    /// <param name="radians">Rotation angles in radians.</param>
    /// <returns>The corresponding ZYX basis.</returns>
    private static Basis ExplicitZyx(Vector3 radians)
    {
        var sx = Mathf.Sin(radians.X);
        var cx = Mathf.Cos(radians.X);
        var sy = Mathf.Sin(radians.Y);
        var cy = Mathf.Cos(radians.Y);
        var sz = Mathf.Sin(radians.Z);
        var cz = Mathf.Cos(radians.Z);

        // Explicit columns of mZ*mY*mX, independently of Basis.FromEuler.
        return new Basis(
            new Vector3(cz * cy, sz * cy, -sy),
            new Vector3((cz * sy * sx) - (sz * cx), (sz * sy * sx) + (cz * cx), cy * sx),
            new Vector3((cz * sy * cx) + (sz * sx), (sz * sy * cx) - (cz * sx), cy * cx));
    }

    /// <summary>Finds each bone's source parent in the already constructed node hierarchy.</summary>
    /// <param name="bones">Bone nodes indexed by source index.</param>
    /// <returns>The source parent index, or null for a skeleton root.</returns>
    private static Dictionary<int, int?> BuildParents(IReadOnlyDictionary<int, Node3D> bones)
    {
        var indices = bones.ToDictionary(pair => pair.Value, pair => pair.Key);
        var parents = new Dictionary<int, int?>();
        foreach (var (index, bone) in bones)
        {
            var parentNode = bone.GetParent() as Node3D;
            parents[index] = parentNode is not null && indices.TryGetValue(parentNode, out var parent) ? parent : null;
        }

        return parents;
    }

    /// <summary>Finds the largest Euclidean difference between matching basis columns.</summary>
    /// <param name="a">First basis.</param>
    /// <param name="b">Second basis.</param>
    /// <returns>The greatest column difference.</returns>
    private static float BasisMaxDelta(Basis a, Basis b) => Math.Max((a.X - b.X).Length(), Math.Max((a.Y - b.Y).Length(), (a.Z - b.Z).Length()));

    /// <summary>Captures pose values used as the starting point of a blend.</summary>
    /// <param name="blendSource">Source frame, or 0xFFFF to retain the live pose.</param>
    private void CaptureBlendSource(int blendSource)
    {
        foreach (var pair in _tracks)
        {
            if (blendSource == 0xffff)
            {
                _blendState.CaptureOrigin(pair.Key);
                continue;
            }

            _blendState.SetOrigin(pair.Key, new BattleBlendPose(
                BattleKeyTrack.Sample(pair.Value[0], blendSource),
                BattleKeyTrack.Sample(pair.Value[1], blendSource),
                BattleKeyTrack.Sample(pair.Value[2], blendSource)));
        }
    }

    /// <summary>Applies fixed-point position, rotation, and scale to one bone node.</summary>
    /// <param name="bone">Source bone index.</param>
    /// <param name="positionFixed">Position in fixed-point source units.</param>
    /// <param name="rotationFixed">Rotation in turn-based fixed-point units.</param>
    /// <param name="scaleFixed">Scale in fixed-point units.</param>
    private void ApplyPose(int bone, Vector3 positionFixed, Vector3 rotationFixed, Vector3 scaleFixed)
    {
        var sx = (int)scaleFixed.X;
        var sy = (int)scaleFixed.Y;
        var sz = (int)scaleFixed.Z;
        var node = _bones[bone];
        var position = BattleModel.Ps1Position(positionFixed);
        var sourceBasis = Basis.FromEuler(rotationFixed * (Mathf.Pi / 2048f), EulerOrder.Zyx);
        var basis = Ps1ToGodot * sourceBasis * Ps1ToGodot;
        var scale = new Vector3(sx, sy, sz) / 4096f;
        var visible = (sx | sy | sz) > HiddenScaleThreshold;
        node.Position = position;
        node.Basis = basis;
        node.Scale = scale;
        node.Visible = visible;
        _transforms.Apply(bone, BattleBoneTransform.Compose(basis, position, scale), visible);
        _blendState.Publish(bone, new BattleBlendPose(positionFixed, rotationFixed, scaleFixed));
    }
}
