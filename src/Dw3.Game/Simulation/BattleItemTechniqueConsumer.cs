namespace Dw3Production.Simulation;

/// <summary>Applies the supported player item-tech families without reading or decrementing inventory stock.</summary>
public static class BattleItemTechniqueConsumer
{
    private const int StatusMask = FighterFlags.Poisoned | FighterFlags.Paralyzed | FighterFlags.Confused | FighterFlags.Asleep;

    /// <summary>Applies one validated item-tech to the requested active party slot or its source-defined side.</summary>
    /// <param name="state">Battle state receiving the effect.</param>
    /// <param name="request">Owner, source technique identity, extracted effect and resolved heal amount.</param>
    /// <param name="activeSlot">Player slot selected by the caller, or the state's active slot when omitted.</param>
    /// <returns>A named result. Refusals never mutate battle state or inventory.</returns>
    public static BattleItemTechniqueUseResult Apply(BattleState state, BattleItemTechniqueRequest request, int? activeSlot = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (request.Owner != BattleSide.Player)
        {
            return BattleItemTechniqueUseResult.OwnerNotPlayer;
        }

        if (!IsSupported(request))
        {
            return BattleItemTechniqueUseResult.UnknownEffect;
        }

        int slot = activeSlot ?? state.ActivePlayer;
        if (!HasValidActionTarget(state, request.Effect, slot))
        {
            return BattleItemTechniqueUseResult.InvalidTarget;
        }

        if (!HasBenefit(state, request, slot))
        {
            return BattleItemTechniqueUseResult.NoBenefit;
        }

        ApplySupported(state, request, slot);
        return BattleItemTechniqueUseResult.Applied;
    }

    /// <summary>Tests the extracted id/effect pairs that dispatch to the source item-tech action.</summary>
    /// <param name="request">Request whose technique and effect must agree.</param>
    /// <returns>Whether the request belongs to a supported item-tech family.</returns>
    private static bool IsSupported(BattleItemTechniqueRequest request) => request.Effect switch
    {
        BattleTechniqueEffect.ItemHeal => request.TechniqueId is >= 0xB8 and <= 0xBC,
        BattleTechniqueEffect.ItemAutoRecovery => request.TechniqueId == 0xBD,
        BattleTechniqueEffect.ItemCurePoison => request.TechniqueId is 0xBE or 0xBF,
        BattleTechniqueEffect.ItemCureParalysis => request.TechniqueId is 0xC0 or 0xC1,
        BattleTechniqueEffect.ItemCureConfusion => request.TechniqueId is 0xC2 or 0xC3,
        BattleTechniqueEffect.ItemCureAll => request.TechniqueId is 0xC4 or 0xC5,
        BattleTechniqueEffect.ItemRevive => request.TechniqueId == 0x64,
        BattleTechniqueEffect.ItemFullRestore => request.TechniqueId == 0x177,
        _ => false,
    };

    /// <summary>Checks the action precondition without treating an empty slot as an item target.</summary>
    /// <param name="state">Battle state containing the submitted owner and active fighter.</param>
    /// <param name="effect">Supported item-tech effect identifier.</param>
    /// <param name="slot">Selected player target slot.</param>
    /// <returns>Whether the action has a living valid active target.</returns>
    private static bool HasValidActionTarget(BattleState state, int effect, int slot)
    {
        if (effect is BattleTechniqueEffect.ItemRevive or BattleTechniqueEffect.ItemFullRestore)
        {
            return state.ActivePlayerFighter is { IsKnockedOut: false };
        }

        return slot >= 0 && slot < state.Players.Length && state.Players[slot] is { IsKnockedOut: false };
    }

    /// <summary>Tests whether the request can change a resource, a status set or the deferred recovery queue.</summary>
    /// <param name="state">Battle state that would receive the effect.</param>
    /// <param name="request">Validated item-tech request.</param>
    /// <param name="slot">Selected living active player slot.</param>
    /// <returns>Whether applying the request would cause a useful state change.</returns>
    private static bool HasBenefit(BattleState state, BattleItemTechniqueRequest request, int slot) => request.Effect switch
    {
        BattleTechniqueEffect.ItemHeal => request.HealAmount > 0 && HealingTargets(state, request.TechniqueId, slot).Any(fighter => fighter.Hp < fighter.MaxHp),
        BattleTechniqueEffect.ItemAutoRecovery => HasAutoRecoveryBenefit(state, slot),
        BattleTechniqueEffect.ItemCurePoison or BattleTechniqueEffect.ItemCureParalysis or BattleTechniqueEffect.ItemCureConfusion or BattleTechniqueEffect.ItemCureAll => CureTargets(state, request.TechniqueId, slot).Any(fighter => (fighter.Flags & StatusFlag(request.Effect)) != 0),
        BattleTechniqueEffect.ItemRevive => state.Players.OfType<BattleFighter>().Any(fighter => fighter.IsKnockedOut),
        BattleTechniqueEffect.ItemFullRestore => state.Players.OfType<BattleFighter>().Any(NeedsFullRestore),
        _ => false,
    };

    /// <summary>Tests whether auto recovery can queue useful deferred work for the selected living fighter.</summary>
    /// <param name="state">Battle state containing the selected player slot.</param>
    /// <param name="slot">Validated living active player slot.</param>
    /// <returns>Whether a living target can receive or refresh the source persistent state.</returns>
    private static bool HasAutoRecoveryBenefit(BattleState state, int slot) =>
        state.Players[slot] is { IsKnockedOut: false };

    /// <summary>Applies a previously validated item-tech family without making a second eligibility decision.</summary>
    /// <param name="state">Battle state receiving the effect.</param>
    /// <param name="request">Validated item-tech request.</param>
    /// <param name="slot">Selected living active player slot.</param>
    private static void ApplySupported(BattleState state, BattleItemTechniqueRequest request, int slot)
    {
        switch (request.Effect)
        {
            case BattleTechniqueEffect.ItemHeal:
                Heal(HealingTargets(state, request.TechniqueId, slot), request.HealAmount);
                return;
            case BattleTechniqueEffect.ItemAutoRecovery:
                state.QueueAutoRecovery(new BattleAutoRecovery(BattleSide.Player, slot, request.TechniqueId));
                return;
            case BattleTechniqueEffect.ItemCurePoison:
            case BattleTechniqueEffect.ItemCureParalysis:
            case BattleTechniqueEffect.ItemCureConfusion:
            case BattleTechniqueEffect.ItemCureAll:
                Cure(CureTargets(state, request.TechniqueId, slot), StatusFlag(request.Effect));
                return;
            case BattleTechniqueEffect.ItemRevive:
                Revive(state.Players.OfType<BattleFighter>());
                return;
            case BattleTechniqueEffect.ItemFullRestore:
                Restore(state.Players.OfType<BattleFighter>());
                return;
        }
    }

    /// <summary>Gets the living target set of a healing action.</summary>
    /// <param name="state">Battle state containing player slots.</param>
    /// <param name="techniqueId">Source technique selecting active or side scope.</param>
    /// <param name="slot">Selected player target slot.</param>
    /// <returns>Living fighters selected by the source healing scope.</returns>
    private static IEnumerable<BattleFighter> HealingTargets(BattleState state, int techniqueId, int slot) =>
        techniqueId >= 0xBB ? Living(state.Players) : SingleLiving(state.Players, slot);

    /// <summary>Gets the status-target set selected by an even active cure or an odd side cure.</summary>
    /// <param name="state">Battle state containing player slots.</param>
    /// <param name="techniqueId">Source technique selecting active or side scope.</param>
    /// <param name="slot">Selected player target slot.</param>
    /// <returns>Living fighters selected by the source cure scope.</returns>
    private static IEnumerable<BattleFighter> CureTargets(BattleState state, int techniqueId, int slot) =>
        (techniqueId & 1) == 0 ? SingleLiving(state.Players, slot) : Living(state.Players);

    /// <summary>Gets a living fighter in one valid slot, otherwise an empty sequence.</summary>
    /// <param name="fighters">Three player slots.</param>
    /// <param name="slot">Candidate slot index.</param>
    /// <returns>The living fighter at the slot, or none.</returns>
    private static IEnumerable<BattleFighter> SingleLiving(BattleFighter?[] fighters, int slot) =>
        slot >= 0 && slot < fighters.Length && fighters[slot] is { IsKnockedOut: false } fighter ? [fighter] : [];

    /// <summary>Gets existing fighters that may receive the source action's living-only effects.</summary>
    /// <param name="fighters">Fighters whose null and knocked-out entries are excluded.</param>
    /// <returns>Every existing living fighter.</returns>
    private static IEnumerable<BattleFighter> Living(IEnumerable<BattleFighter?> fighters) =>
        fighters.OfType<BattleFighter>().Where(fighter => !fighter.IsKnockedOut);

    /// <summary>Maps a cure effect to its source status bit.</summary>
    /// <param name="effect">Supported cure effect identifier.</param>
    /// <returns>The status mask the cure clears.</returns>
    private static int StatusFlag(int effect) => effect switch
    {
        BattleTechniqueEffect.ItemCurePoison => FighterFlags.Poisoned,
        BattleTechniqueEffect.ItemCureParalysis => FighterFlags.Paralyzed,
        BattleTechniqueEffect.ItemCureConfusion => FighterFlags.Confused,
        _ => StatusMask,
    };

    /// <summary>Caps HP gains at each target's maximum.</summary>
    /// <param name="fighters">Living targets that receive the HP amount.</param>
    /// <param name="amount">Resolved nonnegative source heal amount.</param>
    private static void Heal(IEnumerable<BattleFighter> fighters, int amount)
    {
        foreach (BattleFighter fighter in fighters)
        {
            fighter.Hp = Math.Min(fighter.Hp + Math.Max(amount, 0), fighter.MaxHp);
        }
    }

    /// <summary>Clears selected status bits and their matching source counters.</summary>
    /// <param name="fighters">Living targets that receive the cure.</param>
    /// <param name="flag">Status mask selected by the source cure family.</param>
    private static void Cure(IEnumerable<BattleFighter> fighters, int flag)
    {
        foreach (BattleFighter fighter in fighters)
        {
            fighter.Flags &= ~flag;
            if ((flag & FighterFlags.Paralyzed) != 0)
            {
                fighter.Paralysis = 0;
            }

            if ((flag & FighterFlags.Confused) != 0)
            {
                fighter.Confusion = 0;
            }

            if ((flag & FighterFlags.Asleep) != 0)
            {
                fighter.Sleep = 0;
            }
        }
    }

    /// <summary>Brings knocked-out existing partners back at full HP.</summary>
    /// <param name="fighters">Existing partners eligible for revival.</param>
    private static void Revive(IEnumerable<BattleFighter> fighters)
    {
        foreach (BattleFighter fighter in fighters.Where(fighter => fighter.IsKnockedOut))
        {
            fighter.Hp = fighter.MaxHp;
        }
    }

    /// <summary>Fully restores one existing partner and clears every source status and lock bit.</summary>
    /// <param name="fighters">Existing partners eligible for restoration.</param>
    private static void Restore(IEnumerable<BattleFighter> fighters)
    {
        foreach (BattleFighter fighter in fighters)
        {
            fighter.Hp = fighter.MaxHp;
            fighter.Mp = fighter.MaxMp;
            fighter.Flags = 0;
            fighter.Paralysis = 0;
            fighter.Confusion = 0;
            fighter.Sleep = 0;
        }
    }

    /// <summary>Tests whether a full restore would change one partner.</summary>
    /// <param name="fighter">Existing partner inspected for a restorative difference.</param>
    /// <returns>Whether HP, MP, a status or a status counter needs restoration.</returns>
    private static bool NeedsFullRestore(BattleFighter fighter) =>
        fighter.Hp < fighter.MaxHp || fighter.Mp < fighter.MaxMp || fighter.Flags != 0 || fighter.Paralysis != 0 || fighter.Confusion != 0 || fighter.Sleep != 0;
}
