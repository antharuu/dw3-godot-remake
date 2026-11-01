namespace Dw3.Formats.Battle;

/// <summary>The structural half of a battle model: hierarchy and meshes.</summary>
/// <param name="ModelEntry">Archive entry of the model descriptor.</param>
/// <param name="TextureEntry">Archive entry of the model's texture archive.</param>
/// <param name="Meshes">One mesh per non-root bone, in bone order.</param>
public sealed record BattleModelStructure(
    int ModelEntry,
    int TextureEntry,
    IReadOnlyList<BattleModelBone> Bones,
    IReadOnlyList<BattleMesh> Meshes)
{
    /// <summary>Number of bones, root included.</summary>
    public int BoneCount => Bones.Count;
}
