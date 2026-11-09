namespace Dw3Production.Simulation;

/// <summary>Sentinel values shared by elements, families, techniques and effects.
/// Ids below each <c>First</c> value mean "none" in the battle tables, so code compares
/// against these values instead of zero.</summary>
public static class BattleConst
{
    /// <summary>Lowest element id. A technique or boost with a smaller element has no element,
    /// and <c>element - ElementFirst</c> indexes the seven-entry resistance array.</summary>
    public const int ElementFirst = 2;

    /// <summary>Number of elements, which is also the length of a fighter's elemental resistance array.</summary>
    public const int ElementCount = 7;

    /// <summary>Lowest family id. A technique with a smaller family gets no same-family bonus.</summary>
    public const int FamilyFirst = 2;

    /// <summary>Value of a technique's icon field that marks a physical technique, resolved with the physical hit formula and critical roll.</summary>
    public const int TechPhysical = 2;

    /// <summary>Value of a technique's icon field that marks a magic technique, resolved with the spirit formula and the magic critical roll.</summary>
    public const int TechMagic = 3;

    /// <summary>Lowest technique effect id. A technique with a smaller effect only deals damage.</summary>
    public const int TechEffectFirst = 2;

    /// <summary>Technique effect id that raises the critical hit chance.</summary>
    public const int TechEffectCritical = 11;

    /// <summary>Index in an encounter's block flags of the flag that prevents player-originated poison.</summary>
    public const int BlockPoison = 0;

    /// <summary>Index in an encounter's block flags of the flag that prevents player-originated paralysis.</summary>
    public const int BlockParalysis = 1;

    /// <summary>Index in an encounter's block flags of the flag that prevents player-originated confusion.</summary>
    public const int BlockConfusion = 2;

    /// <summary>Index in an encounter's block flags of the flag that prevents player-originated sleep.</summary>
    public const int BlockSleep = 3;

    /// <summary>Index in an encounter's block flags of the flag that prevents player-originated knock-out effects.</summary>
    public const int BlockKnockOut = 4;

    /// <summary>Index in an encounter's block flags of the flag that prevents player-originated drain effects.</summary>
    public const int BlockDrain = 5;

    /// <summary>Index in an encounter's block flags of the flag that prevents player-originated steal effects.</summary>
    public const int BlockSteal = 7;

    /// <summary>Index in an encounter's block flags of the flag that prevents player-originated attack lowering.</summary>
    public const int BlockLowerAttack = 8;

    /// <summary>Index in an encounter's block flags of the flag that prevents player-originated defense lowering.</summary>
    public const int BlockLowerDefense = 9;

    /// <summary>Index in an encounter's block flags of the flag that prevents player-originated speed lowering.</summary>
    public const int BlockLowerSpeed = 10;

    /// <summary>Elemental resistance slot paired with poison resistance.</summary>
    public const int PoisonElementResistance = 1;

    /// <summary>Index in an encounter's block flags of the flag that forbids running away.</summary>
    public const int BlockRunAway = 11;
}
