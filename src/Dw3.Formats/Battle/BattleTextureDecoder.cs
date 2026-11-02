namespace Dw3.Formats.Battle;

/// <summary>Rebuilds a model texture archive's local VRAM and decodes consumer-selected PS1 pages.</summary>
public static class BattleTextureDecoder
{
    /// <summary>
    /// Builds the local VRAM from <paramref name="archive"/>, then decodes one texture page per
    /// distinct tpage/CLUT combination used by <paramref name="consumers"/>.
    /// </summary>
    /// <param name="archive">Raw bytes of the model's texture archive (multi-entry TIM table).</param>
    /// <param name="consumers">Texture state of every textured polygon that references this archive.</param>
    /// <returns>
    /// A <see cref="BattleTextureSet"/> with one entry per distinct combination, keyed in first-use order.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="archive"/> or <paramref name="consumers"/> is null.</exception>
    /// <exception cref="FormatException">The archive is malformed, a polygon uses an unsupported tpage mode, or no textured polygon is present.</exception>
    public static BattleTextureSet Decode(byte[] archive, IEnumerable<MeshTextureState> consumers)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(consumers);
        BattleLocalVram vram = BattleLocalVram.Build(archive);
        Dictionary<BattleTextureCombination, int> keys = CollectCombinations(consumers);
        var textures = keys.OrderBy(pair => pair.Value)
            .Select(pair => BattleTexturePageDecoder.Decode(vram, pair.Key, pair.Value))
            .ToArray();
        return new BattleTextureSet(textures, vram.Layout, keys);
    }

    /// <summary>Assigns a texture key, in first-use order, to every distinct combination the polygons select.</summary>
    /// <param name="consumers">Texture state of every textured polygon.</param>
    /// <returns>Combination to key map.</returns>
    /// <exception cref="FormatException">A polygon uses a tpage mode other than 0..2, or no polygon is textured.</exception>
    private static Dictionary<BattleTextureCombination, int> CollectCombinations(IEnumerable<MeshTextureState> consumers)
    {
        var keys = new Dictionary<BattleTextureCombination, int>();
        foreach (MeshTextureState state in consumers)
        {
            int mode = state.TpageMode;
            if (mode is < 0 or > 2)
            {
                throw new FormatException($"unsupported PS1 tpage mode {mode}");
            }

            keys.TryAdd(BattleTextureCombination.From(state), keys.Count);
        }

        if (keys.Count == 0)
        {
            throw new FormatException("battle model has no textured polygon combinations");
        }

        return keys;
    }
}
