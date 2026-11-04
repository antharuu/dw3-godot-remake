using Godot;

namespace Dw3Production.Presentation;

/// <summary>
/// Retains source-space world transforms for battle bones. A locally hidden bone keeps its last
/// visible matrix, while an independently visible child composes against that retained matrix.
/// </summary>
internal sealed class BattleSkeletonTransformCache
{
    private readonly IReadOnlyDictionary<int, int?> _parents;
    private readonly Dictionary<int, Transform3D> _world;

    /// <summary>
    /// Initializes a new instance of the <see cref="BattleSkeletonTransformCache"/> class with
    /// identity transforms for every known bone.
    /// </summary>
    /// <param name="parents">Parent bone index by bone index, or null for a root bone.</param>
    public BattleSkeletonTransformCache(IReadOnlyDictionary<int, int?> parents)
    {
        _parents = parents;
        _world = parents.Keys.ToDictionary(index => index, _ => Transform3D.Identity);
    }

    /// <summary>
    /// Applies a locally visible bone transform. Hidden bones deliberately retain their previous
    /// world transform because the source skips their matrix update.
    /// </summary>
    /// <param name="bone">The source bone index.</param>
    /// <param name="local">The newly sampled local transform.</param>
    /// <param name="visible">Whether the source scale predicate leaves the bone visible.</param>
    public void Apply(int bone, Transform3D local, bool visible)
    {
        if (!visible)
        {
            return;
        }

        _world[bone] = _parents[bone] is int parent ? _world[parent] * local : local;
    }

    /// <summary>Gets a bone's retained source-space world transform.</summary>
    /// <param name="bone">The source bone index.</param>
    /// <returns>The last visible world transform.</returns>
    public Transform3D World(int bone) => _world[bone];
}
