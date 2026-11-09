namespace Dw3Production.Simulation;

/// <summary>Selects the source-defined Double Magic damage records while retaining its primary accuracy record.</summary>
internal static class DoubleMagicTechniqueSelector
{
    /// <summary>Returns whether a Double Magic action has both source-defined damage records available before it spends resources.</summary>
    /// <param name="technique">Technique attempting to execute.</param>
    /// <param name="catalogue">Loaded technique records indexed by source id.</param>
    /// <returns><see langword="true"/> for non-Double-Magic actions or when both required damage entries are loaded.</returns>
    public static bool HasRequiredDamageEntries(TechData technique, IReadOnlyDictionary<int, TechData> catalogue) =>
        technique.Effect != BattleTechniqueEffect.DoubleMagic
        || (catalogue.ContainsKey(BattleTechniqueEffect.DoubleMagicFirstDamageTechniqueId)
            && catalogue.ContainsKey(BattleTechniqueEffect.DoubleMagicSecondDamageTechniqueId));

    /// <summary>Returns the source damage record selected for one hit while preserving the primary technique for accuracy.</summary>
    /// <param name="technique">Primary action technique.</param>
    /// <param name="catalogue">Loaded technique records indexed by source id.</param>
    /// <param name="hitIndex">Zero-based hit index.</param>
    /// <returns>The primary technique or its source-defined alternate damage entry.</returns>
    public static TechData DamageTechniqueForHit(TechData technique, IReadOnlyDictionary<int, TechData> catalogue, int hitIndex)
    {
        if (technique.Effect != BattleTechniqueEffect.DoubleMagic)
        {
            return technique;
        }

        int id = hitIndex == 0
            ? BattleTechniqueEffect.DoubleMagicFirstDamageTechniqueId
            : BattleTechniqueEffect.DoubleMagicSecondDamageTechniqueId;
        return catalogue[id];
    }
}
