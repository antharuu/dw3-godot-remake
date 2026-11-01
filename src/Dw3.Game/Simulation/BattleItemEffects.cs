namespace Dw3Production.Simulation;

/// <summary>
/// Battle item effects by behaviour key. heal restores a set amount of HP to the active fighter;
/// revive restores all knocked-out partners to full HP; restoreHalfHpMp recovers half HP and MP.
/// The rest (cureStatus, boostSpeed/Attack/Defense, special, fillGauge, boostElement, drain,
/// confuse, weakenDefense, lowerStat, lowerSpeed, itemTech) are not modelled yet and are
/// consumed without effect, a named simplification.
/// </summary>
public static class BattleItemEffects
{
    /// <summary>Applies <paramref name="item"/>'s effect. HP and MP never rise above their maximum.</summary>
    /// <param name="item">The item used; only its effect key and amount are read.</param>
    /// <param name="active">The active partner, or <see langword="null"/> when there is none.</param>
    /// <param name="party">The three party slots, for the party-wide revive.</param>
    public static void Apply(BattleItem item, BattleFighter? active, IEnumerable<BattleFighter?> party)
    {
        switch (item.Effect)
        {
            case "heal":
                active?.Hp = Math.Min(active.Hp + item.Amount, active.MaxHp);
                break;
            case "revive":
                Revive(party);
                break;
            case "restoreHalfHpMp":
                RestoreHalf(active);
                break;
        }
    }

    /// <summary>Brings every knocked-out party member (HP exactly 0) back at full HP.</summary>
    /// <param name="party">The three party slots; empty slots are skipped.</param>
    private static void Revive(IEnumerable<BattleFighter?> party)
    {
        foreach (var player in party.OfType<BattleFighter>().Where(player => player is { Id: not 0, Hp: 0 }))
        {
            player.Hp = player.MaxHp;
        }
    }

    /// <summary>Recovers half of the maximum HP and half of the maximum MP, capped at the maximum.</summary>
    /// <param name="fighter">The active partner, or <see langword="null"/> (nothing happens).</param>
    private static void RestoreHalf(BattleFighter? fighter)
    {
        if (fighter == null)
        {
            return;
        }

        fighter.Hp = Math.Min(fighter.Hp + (fighter.MaxHp / 2), fighter.MaxHp);
        fighter.Mp = Math.Min(fighter.Mp + (fighter.MaxMp / 2), fighter.MaxMp);
    }
}
