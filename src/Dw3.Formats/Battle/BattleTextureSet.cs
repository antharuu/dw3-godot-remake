namespace Dw3.Formats.Battle;

public sealed class BattleTextureSet
{
    private readonly IReadOnlyDictionary<(int Mode, int Page, int? X, int? Y), int> _keys;
    internal BattleTextureSet(IReadOnlyList<BattleTexture> textures, BattleTextureVram vram,
        IReadOnlyDictionary<(int, int, int?, int?), int> keys)
        => (Textures, Vram, _keys) = (textures, vram, keys);
    public IReadOnlyList<BattleTexture> Textures { get; }
    public BattleTextureVram Vram { get; }
    public int KeyOf(MeshTextureState state)
    {
        int? x = state.TpageMode is 0 or 1 ? state.ClutX : null;
        int? y = state.TpageMode is 0 or 1 ? state.ClutY : null;
        return _keys.TryGetValue((state.TpageMode, state.Page, x, y), out int key)
            ? key : throw new FormatException("Polygon texture combination was not decoded.");
    }
}
