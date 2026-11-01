namespace Dw3Production.Simulation;

/// <summary>Sentinel values shared by elements, families, techniques and effects.</summary>
public static class BattleConst
{
    public const int ElementFirst = 2;
    public const int ElementCount = 7;
    public const int FamilyFirst = 2;
    public const int TechPhysical = 2; // TechData.icon
    public const int TechMagic = 3; // TechData.icon
    public const int TechEffectFirst = 2;
    public const int TechEffectCritical = 11;
    public const int BlockRunAway = 11; // BattleSetup.blocks index
}
