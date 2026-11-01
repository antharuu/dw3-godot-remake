using Godot;

namespace Dw3Production.Presentation;

/// <summary>
/// One Godot mesh surface of a battle model: every primitive of one bone that shares a texture
/// page and blend mode. The per-vertex arrays are retained in emission order (triangles, a quad
/// expanded to six corners) so lighting can rebuild the mesh colours.
/// </summary>
internal sealed class BattleLitSurface
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BattleLitSurface"/> class.
    /// </summary>
    /// <param name="instance">Mesh instance parented to the bone node.</param>
    /// <param name="bone">Source bone index the surface follows.</param>
    /// <param name="positions">Emitted vertex positions in bone-local Godot units.</param>
    /// <param name="uvs">Emitted texture coordinates, source texels divided by 256.</param>
    /// <param name="lighting">Per-vertex lighting inputs, same length as <paramref name="positions"/>.</param>
    /// <param name="material">PS1 shader material bound to the surface.</param>
    /// <param name="abr">Raw draw-state blend value: 0 opaque, 1..4 select PSX ABR modes 0..3.</param>
    public BattleLitSurface(
        MeshInstance3D instance,
        int bone,
        Vector3[] positions,
        Vector2[] uvs,
        BattleLightingVertex[] lighting,
        ShaderMaterial material,
        int abr)
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

    /// <summary>Gets the mesh instance whose mesh is rebuilt on every lighting refresh.</summary>
    public MeshInstance3D Instance { get; }

    /// <summary>Gets the source bone index whose world basis rotates the normals.</summary>
    public int Bone { get; }

    /// <summary>Gets the emitted vertex positions in bone-local Godot units.</summary>
    public Vector3[] Positions { get; }

    /// <summary>Gets the emitted texture coordinates in the 0..1 range of a 256x256 page.</summary>
    public Vector2[] Uvs { get; }

    /// <summary>Gets the lighting inputs of every emitted vertex.</summary>
    public BattleLightingVertex[] Lighting { get; }

    /// <summary>Gets the PS1 shader material, kept so a probe can swap its cull mode.</summary>
    public ShaderMaterial Material { get; }

    /// <summary>Gets the colours computed by the latest lighting refresh, one per emitted vertex.</summary>
    public Color[] Colors { get; }

    /// <summary>Gets the raw draw-state blend value: 0 means opaque, 1..4 select PSX ABR 0..3.</summary>
    public int Abr { get; }
}
