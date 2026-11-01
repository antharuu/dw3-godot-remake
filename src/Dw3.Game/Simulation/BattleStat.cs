namespace Dw3Production.Simulation;

/// <summary>BATTLE_STAT_* indices into BattleStats.Stats (battle.h).</summary>
public static class BattleStat
{
    public const int Attack = 0; // physical damage
    public const int Defense = 1; // against it
    public const int Spirit = 2; // magic damage, and against it
    public const int Wisdom = 3; // magic accuracy, statuses and heals
    public const int Speed = 4; // physical accuracy, evasion and running away
}
