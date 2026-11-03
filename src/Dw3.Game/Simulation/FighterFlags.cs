namespace Dw3Production.Simulation;

/// <summary>Bits of <see cref="BattleFighter.Flags"/>: the statuses a technique can inflict and
/// the two locks an encounter or technique can put on a fighter. Several bits can be set at once.</summary>
public static class FighterFlags
{
    /// <summary>Poisoned status bit.</summary>
    public const int Poisoned = 1;

    /// <summary>Paralyzed status bit. Halves the run away chance and adds an eighth of the paralysis counter to criticals taken.</summary>
    public const int Paralyzed = 2;

    /// <summary>Confused status bit. Adds half of the confusion counter to criticals taken.</summary>
    public const int Confused = 4;

    /// <summary>Asleep status bit. Greys attack, technique and digivolve, forbids running away, and adds an eighth of the sleep counter to criticals taken.</summary>
    public const int Asleep = 8;

    /// <summary>Lock bit that forbids the fighter from switching out or running away.</summary>
    public const int NoSwitch = 0x10;

    /// <summary>Lock bit that greys the Digivolve command and refuses any digivolution.</summary>
    public const int NoDigivolve = 0x20;
}
