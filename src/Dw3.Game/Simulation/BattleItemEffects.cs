namespace Dw3Production.Simulation;

/// <summary>
/// Applies the supported battle-item families through the established item-tech and status contracts.
/// An unsupported item, invalid target, or item with no possible state change is refused before the
/// flow controller can consume its stock.
/// </summary>
public static class BattleItemEffects
{
    /// <summary>Attempts an item effect without changing inventory or advancing the battle turn.</summary>
    /// <param name="item">Source-classified item selected by the player.</param>
    /// <param name="state">Battle state whose player party receives the item effect.</param>
    /// <param name="rng">Shared battle random source for temporary effect expiry timing.</param>
    /// <returns>Whether the supported item made a valid state change.</returns>
    public static bool TryApply(BattleItem item, BattleState state, IBattleRng rng)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(rng);
        switch (item.Effect)
        {
            case "heal":
                return TryApplyItemTechnique(state, item, HealRequest(item));
            case "cureStatus":
                return TryApplyItemTechnique(state, item, CureRequest(item));
            case "revive":
                return TryApplyItemTechnique(state, item, new(0x64, BattleTechniqueEffect.ItemRevive, 0));
            case "restoreHalfHpMp":
                return TryRestoreHalf(state, item);
            case "boostSpeed":
                return BattlePartnerItemEffects.TryBoostSpeed(state, item, rng);
            case "boostAttack":
                return BattlePartnerItemEffects.TryBoostAttack(state, item, rng);
            case "boostDefense":
                return BattlePartnerItemEffects.TryBoostDefense(state, item, rng);
            case "special":
                return BattlePartnerItemEffects.TrySpecial(state, item, rng);
            case "fillGauge":
                return BattlePartnerItemEffects.TryFillGauge(state, item);
            default:
                return false;
        }
    }

    /// <summary>Builds the verified source item-tech request for one HP recovery item.</summary>
    /// <param name="item">Healing item whose id selects the source action.</param>
    /// <returns>The mapped technique id, effect and decoded HP amount.</returns>
    private static ItemTechniqueRequest HealRequest(BattleItem item) =>
        new(item.Id is >= 0x2B and <= 0x2E ? item.Id + 0x8D : 0xB8, BattleTechniqueEffect.ItemHeal, item.Amount);

    /// <summary>Builds the verified source item-tech request for one status cure item.</summary>
    /// <param name="item">Cure item whose id selects the status action.</param>
    /// <returns>The mapped technique id, effect and zero heal amount.</returns>
    private static ItemTechniqueRequest CureRequest(BattleItem item) => item.Id switch
    {
        0x42 => new(0xBE, BattleTechniqueEffect.ItemCurePoison, 0),
        0x43 => new(0xC0, BattleTechniqueEffect.ItemCureParalysis, 0),
        0x44 => new(0xC2, BattleTechniqueEffect.ItemCureConfusion, 0),
        0x45 => new(0xC4, BattleTechniqueEffect.ItemCureAll, 0),
        _ => ItemTechniqueRequest.Unsupported,
    };

    /// <summary>Applies a mapped item-tech request only when the mapping is supported and beneficial.</summary>
    /// <param name="state">Battle state receiving the request.</param>
    /// <param name="item">Item providing target restrictions.</param>
    /// <param name="request">Mapped source item-tech action.</param>
    /// <returns>Whether the source item-tech consumer applied its effect.</returns>
    private static bool TryApplyItemTechnique(BattleState state, BattleItem item, ItemTechniqueRequest request)
    {
        if (request == ItemTechniqueRequest.Unsupported || item.Target is not ("partner" or "party"))
        {
            return false;
        }

        return BattleItemTechniqueConsumer.Apply(state, new(BattleSide.Player, request.TechniqueId, request.Effect, request.HealAmount))
            == BattleItemTechniqueUseResult.Applied;
    }

    /// <summary>Recovers half of the active partner's maximum HP and MP, capped at each maximum.</summary>
    /// <param name="state">Battle state supplying the active partner.</param>
    /// <param name="item">Item whose source target must be a partner or party.</param>
    /// <returns>Whether recovery changes at least one active living resource.</returns>
    private static bool TryRestoreHalf(BattleState state, BattleItem item)
    {
        BattleFighter? fighter = state.ActivePlayerFighter;
        if (item.Target is not ("partner" or "party") || fighter is not { IsKnockedOut: false })
        {
            return false;
        }

        int recoveredHp = Math.Min(fighter.Hp + (fighter.MaxHp / 2), fighter.MaxHp);
        int recoveredMp = Math.Min(fighter.Mp + (fighter.MaxMp / 2), fighter.MaxMp);
        if (recoveredHp == fighter.Hp && recoveredMp == fighter.Mp)
        {
            return false;
        }

        fighter.Hp = recoveredHp;
        fighter.Mp = recoveredMp;
        return true;
    }

    /// <summary>Represents one verified item-tech action selected by an item id.</summary>
    /// <param name="TechniqueId">Source item-tech id.</param>
    /// <param name="Effect">Established item-tech effect id.</param>
    /// <param name="HealAmount">Decoded HP recovery amount.</param>
    private readonly record struct ItemTechniqueRequest(int TechniqueId, int Effect, int HealAmount)
    {
        /// <summary>Gets a sentinel for an item with no supported source action.</summary>
        public static ItemTechniqueRequest Unsupported { get; } = new(0, 0, 0);
    }
}
