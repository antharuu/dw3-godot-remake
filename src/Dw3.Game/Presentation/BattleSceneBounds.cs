using Godot;

namespace Dw3Production.Presentation;

/// <summary>World-space bounding boxes of battle scene subtrees, measured over their mesh instances.</summary>
internal static class BattleSceneBounds
{
    /// <summary>Measures the meshes that are visible in the tree.</summary>
    /// <param name="root">Subtree root.</param>
    /// <returns>The merged world box.</returns>
    /// <exception cref="InvalidOperationException">The subtree has no visible mesh.</exception>
    public static Aabb Of(Node3D root) => Of(root, includeInvisible: false);

    /// <summary>Measures the meshes of a subtree.</summary>
    /// <param name="root">Subtree root.</param>
    /// <param name="includeInvisible">True to include meshes hidden in the tree. A mesh's own
    /// Visible flag is local; visibility in the tree also reflects its bone's scale threshold.</param>
    /// <returns>The merged world box.</returns>
    /// <exception cref="InvalidOperationException">No mesh is included.</exception>
    public static Aabb Of(Node3D root, bool includeInvisible)
    {
        Aabb? result = null;
        foreach (var child in Descendants(root))
        {
            if (child is not MeshInstance3D mesh || mesh.Mesh is null || (!includeInvisible && !mesh.IsVisibleInTree()))
            {
                continue;
            }

            var bounds = mesh.GlobalTransform * mesh.GetAabb();
            result = result is null ? bounds : result.Value.Merge(bounds);
        }

        return result ?? throw new InvalidOperationException($"node {root.Name} has no {(includeInvisible ? "mesh" : "visible mesh")} bounds");
    }

    /// <summary>Counts the mesh instances of a subtree that carry a mesh, hidden ones included.</summary>
    /// <param name="root">Subtree root.</param>
    /// <returns>The count.</returns>
    public static int MeshCount(Node3D root) => Descendants(root).Count(node => node is MeshInstance3D { Mesh: not null });

    /// <summary>Enumerates every descendant node below a root in depth-first order.</summary>
    /// <param name="node">Root whose descendants are enumerated.</param>
    /// <returns>All descendants of <paramref name="node"/>.</returns>
    private static IEnumerable<Node> Descendants(Node node)
    {
        foreach (Node child in node.GetChildren())
        {
            yield return child;
            foreach (var descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }
}
