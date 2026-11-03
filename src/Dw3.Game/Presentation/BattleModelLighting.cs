using Godot;

namespace Dw3Production.Presentation;

/// <summary>
/// Recomputes a battle model's vertex colours from the bound stage light set, like the PS1 GTE
/// normal-colour step: ambient plus each light's colour times the clamped dot product with the
/// normal, the normal being composed by the full bone world basis. Flat primitives keep their
/// command colour unless a model colour override replaces it.
/// </summary>
internal sealed class BattleModelLighting
{
    private bool _capturedVertex;

    /// <summary>Gets or sets the bound stage light set; null keeps the baked fallback colours.</summary>
    public BattleModel.BattleLightSet? LightSet { get; set; }

    /// <summary>Gets or sets the colour replacing every flat primitive's command colour; null restores it.</summary>
    public Color? ModelColor { get; set; }

    /// <summary>Gets a description of the first lit vertex of the latest refresh, for diagnostics.</summary>
    public string VertexSummary { get; private set; } = "vertex=unavailable";

    /// <summary>Rebuilds the mesh of every surface with freshly lit colours.</summary>
    /// <param name="surfaces">Surfaces to recolour.</param>
    /// <param name="skeleton">Skeleton whose live bone bases rotate the normals.</param>
    /// <param name="fighterId">Fighter id written into <see cref="VertexSummary"/>.</param>
    public void Refresh(IReadOnlyList<BattleLitSurface> surfaces, BattleSkeleton skeleton, int fighterId)
    {
        _capturedVertex = false;
        foreach (var surface in surfaces)
        {
            RefreshSurface(surface, skeleton.Bone(surface.Bone).GlobalTransform.Basis, fighterId);
        }
    }

    /// <summary>Calculates the clamped lit colour for one transformed surface normal.</summary>
    /// <param name="set">Ambient and directional stage lights.</param>
    /// <param name="normal">Surface normal in world coordinates.</param>
    /// <returns>The resulting colour.</returns>
    private static Color Shade(BattleModel.BattleLightSet set, Vector3 normal)
    {
        var color = set.Ambient;
        foreach (var light in set.Lights)
        {
            color += light.Rgb * Mathf.Max(0f, light.Direction.Dot(normal));
        }

        return new Color(Mathf.Min(1f, color.R), Mathf.Min(1f, color.G), Mathf.Min(1f, color.B));
    }

    /// <summary>Rebuilds one surface mesh with colours calculated from its live bone basis.</summary>
    /// <param name="surface">Surface to recolour.</param>
    /// <param name="basis">World basis of the surface's owning bone.</param>
    /// <param name="fighterId">Fighter identifier used for diagnostics.</param>
    private void RefreshSurface(BattleLitSurface surface, Basis basis, int fighterId)
    {
        var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        for (var index = 0; index < surface.Positions.Length; index++)
        {
            var color = VertexColor(surface, index, basis, fighterId);
            tool.SetUV(surface.Uvs[index]);
            tool.SetColor(color);
            tool.AddVertex(surface.Positions[index]);
            surface.Colors[index] = color;
        }

        surface.Instance.Mesh = tool.Commit();
        surface.Instance.MaterialOverride = surface.Material;
    }

    /// <summary>Calculates one surface vertex colour and records the first lit diagnostic sample.</summary>
    /// <param name="surface">Surface containing the vertex.</param>
    /// <param name="index">Zero-based vertex index.</param>
    /// <param name="basis">World basis of the surface's owning bone.</param>
    /// <param name="fighterId">Fighter identifier used for diagnostics.</param>
    /// <returns>The colour assigned to the vertex.</returns>
    private Color VertexColor(BattleLitSurface surface, int index, Basis basis, int fighterId)
    {
        var source = surface.Lighting[index];
        if (!source.Lit || LightSet is not { } set)
        {
            return !source.Lit && ModelColor is { } modelColor ? modelColor : source.Flat;
        }

        // The full bone matrix, fixed-point scale included, feeds the normal-colour step.
        // Do not renormalise here: that would erase animated bone scale and brighten or
        // shadow normals unlike the PS1.
        var normal = basis * source.Normal;
        var color = Shade(set, normal);
        if (!_capturedVertex)
        {
            VertexSummary = $"fighter={fighterId} bone={surface.Bone} vertex={index} " +
                $"normal=({normal.X:0.###},{normal.Y:0.###},{normal.Z:0.###}) " +
                $"color=({color.R:0.###},{color.G:0.###},{color.B:0.###})";
            _capturedVertex = true;
        }

        return color;
    }
}
