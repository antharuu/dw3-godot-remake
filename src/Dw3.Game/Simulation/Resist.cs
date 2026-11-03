namespace Dw3Production.Simulation;

/// <summary>Indices into <see cref="BattleStats.Resist"/>: 0..6 are the elemental resistances
/// (element - <see cref="BattleConst.ElementFirst"/>), 7..11 the status resistances named here.</summary>
public static class Resist
{
    /// <summary>Index of the resistance to poison.</summary>
    public const int Poison = 7;

    /// <summary>Index of the resistance to paralysis.</summary>
    public const int Paralysis = 8;

    /// <summary>Index of the resistance to confusion.</summary>
    public const int Confusion = 9;

    /// <summary>Index of the resistance to sleep.</summary>
    public const int Sleep = 10;

    /// <summary>Index of the resistance to instant knock-out effects.</summary>
    public const int KnockOut = 11;

    /// <summary>Length of the resistance array: seven elements plus five statuses.</summary>
    public const int Count = 12;
}
