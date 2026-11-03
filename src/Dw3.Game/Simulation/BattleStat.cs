namespace Dw3Production.Simulation;

/// <summary>Indices of the five battle stats in <see cref="BattleStats.Stats"/>, in the order the
/// fighter and Digimon tables store them.</summary>
public static class BattleStat
{
    /// <summary>Index of the attack stat, which drives physical damage.</summary>
    public const int Attack = 0;

    /// <summary>Index of the defense stat, which reduces physical damage taken.</summary>
    public const int Defense = 1;

    /// <summary>Index of the spirit stat, which drives magic damage and reduces magic damage taken.</summary>
    public const int Spirit = 2;

    /// <summary>Index of the wisdom stat, which drives magic accuracy, status infliction and heals.</summary>
    public const int Wisdom = 3;

    /// <summary>Index of the speed stat, which drives physical accuracy, evasion and running away.</summary>
    public const int Speed = 4;
}
