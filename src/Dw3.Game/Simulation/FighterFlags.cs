namespace Dw3Production.Simulation;

/// <summary>FIGHTER_* flags (battle.h): the statuses a technique can inflict.</summary>
public static class FighterFlags
{
    public const int Poisoned = 1;
    public const int Paralyzed = 2;
    public const int Confused = 4;
    public const int Asleep = 8;
    public const int NoSwitch = 0x10;
    public const int NoDigivolve = 0x20;
}
