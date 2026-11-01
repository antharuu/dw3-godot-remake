namespace Dw3Production.Simulation;

/// <summary>One enemy battle-table action (battle.h BattleTableAction): its target kind
/// (FIGHTSTG_getEnemyAction), the condition that must hold, and the condition's argument.
/// A value type like the source's 4-byte struct, so a default action is (0,0,0) = none.</summary>
public readonly record struct BattleTableAction(int Target, int Condition, int ConditionArg);

/// <summary>An enemy of a battle (game_state.h BattleEnemy).</summary>
public sealed class BattleEnemy
{
    public int Fighter;     // 0 for none
    public int Level;
    public int Hp;
    public int Mp;
    public int Strength;    // in 16ths: scales stats and techniques' power
    public BattleStats BaseStats = new();   // raw battle-table entry (stats/resist/family)
    public int[] Techs = new int[3];        // battle-table techs[0..2]
    public BattleTableAction[] Actions = []; // the four actions (0..2 conditional, 3 fallback)
    public BattleTableAction Counter;        // its counter-attack (condition 0 = none)
}

/// <summary>
/// One of the party's three partners. An adaptation of GAME.partners read through
/// getPartyMember/getPartnerStats, which the core does not model yet: a null entry is an
/// empty party slot, and Stats is injected resolved (the source computes it from partner
/// totals + equipment, which are out of scope).
/// </summary>
public sealed class PartnerSeed
{
    public int Id;          // DIGIMON_DATA[partner].id (overridden by the digimon arg in slot 0)
    public int Hp;
    public int MaxHp;
    public int Mp;
    public int MaxMp;
    public BattleStats Stats = new();
}

/// <summary>A technique (game_state.h TechData), the data the damage math reads.</summary>
public sealed class TechData
{
    public int Mp;
    public int Power;
    public int Icon;        // BattleConst.TechPhysical / TechMagic
    public int Kind;        // 3 = heals the target
    public int Accuracy;
    public int Element;
    public int ElementPower;
    public int Family;
    public int Effect;
    public int EffectChance;
    public int EffectPower;
    public int ScriptStage;
    public int ScriptEffect;
    public int ScriptSound;
    public int Script;      // 5 and 12 are special
    public int HitCount;
}

/// <summary>
/// One battle-usable item, as the simulation core consumes it (BattleItemData
/// in engine-free form). Effect is the battle item's behaviour key ("heal", "revive",
/// "restoreHalfHpMp", "cureStatus", ...); Target is whom the effect acts on;
/// Amount is its power (ItemEffect.amount).
/// </summary>
public sealed record BattleItem(int Id, string Effect, string Target, int Amount);

/// <summary>The next battle (game_state.h BattleSetup).</summary>
public sealed class BattleSetup
{
    public bool RandomBattles;
    public int Stage;
    public int Battle;
    public int Music;
    public BattleEnemy?[] Enemies = new BattleEnemy?[3];
    public PartnerSeed?[] Partners = new PartnerSeed?[3];
    public int AmbushChance;
    public int EncounterKind;
    public int[] Blocks = new int[12];      // BattleConst.BlockRunAway, ...
    public bool HasPrize;
    public int Prize;
}
