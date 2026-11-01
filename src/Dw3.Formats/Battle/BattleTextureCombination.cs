namespace Dw3.Formats.Battle;

/// <summary>
/// One distinct texture used by a model's polygons: tpage colour mode, texture page and, for the
/// palette modes, the CLUT position. Polygons with equal combinations share one decoded texture.
/// </summary>
/// <param name="Mode">Tpage colour mode: 0 = 4-bit, 1 = 8-bit, 2 = 15-bit direct.</param>
/// <param name="Page">Texture page; its VRAM column is Page × 0x40 halfwords.</param>
/// <param name="ClutX">CLUT column in halfwords for modes 0 and 1, null for direct colour.</param>
/// <param name="ClutY">CLUT row for modes 0 and 1, null for direct colour.</param>
internal readonly record struct BattleTextureCombination(int Mode, int Page, int? ClutX, int? ClutY)
{
    /// <summary>Builds the combination a polygon's texture state selects; the CLUT is ignored for direct colour.</summary>
    /// <param name="state">Texture state current when the polygon was emitted.</param>
    /// <returns>The combination key.</returns>
    public static BattleTextureCombination From(MeshTextureState state)
    {
        bool paletted = state.TpageMode is 0 or 1;
        return new BattleTextureCombination(
            state.TpageMode, state.Page, paletted ? state.ClutX : null, paletted ? state.ClutY : null);
    }
}
