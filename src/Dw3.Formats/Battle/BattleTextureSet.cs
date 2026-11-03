namespace Dw3.Formats.Battle;

/// <summary>
/// The complete decoded texture set for one battle model: all texture pages cut from the model's
/// local VRAM, the VRAM layout that produced them, and a lookup map from polygon texture state
/// to per-page key.
/// </summary>
public sealed class BattleTextureSet
{
    private readonly IReadOnlyDictionary<BattleTextureCombination, int> _keys;

    /// <summary>Initializes a new instance of the <see cref="BattleTextureSet"/> class.</summary>
    /// <param name="textures">Decoded textures, indexed by their key.</param>
    /// <param name="vram">Layout of the local VRAM the textures were cut from.</param>
    /// <param name="keys">Texture key of every decoded combination.</param>
    internal BattleTextureSet(
        IReadOnlyList<BattleTexture> textures,
        BattleTextureVram vram,
        IReadOnlyDictionary<BattleTextureCombination, int> keys)
        => (Textures, Vram, _keys) = (textures, vram, keys);

    /// <summary>Gets the decoded texture pages, in first-use key order.</summary>
    public IReadOnlyList<BattleTexture> Textures { get; }

    /// <summary>Gets the local VRAM layout from which these textures were decoded.</summary>
    public BattleTextureVram Vram { get; }

    /// <summary>Returns the texture key for <paramref name="state"/>, or throws if the combination was not decoded.</summary>
    /// <param name="state">The polygon texture state to look up.</param>
    /// <returns>The zero-based texture key assigned during decoding.</returns>
    /// <exception cref="FormatException">The combination was not present when the texture set was built.</exception>
    public int KeyOf(MeshTextureState state)
        => _keys.TryGetValue(BattleTextureCombination.From(state), out int key)
            ? key : throw new FormatException("Polygon texture combination was not decoded.");
}
