namespace Dw3.Formats.Battle;

public sealed class BattleTextureSet
{
    private readonly IReadOnlyDictionary<BattleTextureCombination, int> _keys;

    /// <summary>Initializes a new instance of the <see cref="BattleTextureSet"/> class.</summary>
    /// <param name="textures">Decoded textures, indexed by their key.</param>
    /// <param name="vram">Layout of the local VRAM the textures were cut from.</param>
    /// <param name="keys">Texture key of every decoded combination.</param>
    internal BattleTextureSet(IReadOnlyList<BattleTexture> textures, BattleTextureVram vram,
        IReadOnlyDictionary<BattleTextureCombination, int> keys)
        => (Textures, Vram, _keys) = (textures, vram, keys);

    public IReadOnlyList<BattleTexture> Textures { get; }

    public BattleTextureVram Vram { get; }

    public int KeyOf(MeshTextureState state)
        => _keys.TryGetValue(BattleTextureCombination.From(state), out int key)
            ? key : throw new FormatException("Polygon texture combination was not decoded.");
}
