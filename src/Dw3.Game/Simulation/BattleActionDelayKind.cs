namespace Dw3Production.Simulation;

/// <summary>Identifies a qualified battle event-delay family.</summary>
public enum BattleActionDelayKind
{
    /// <summary>Speed-scaled delay after an ordinary turn action.</summary>
    NormalTurn,

    /// <summary>Speed-scaled delay after an unsuccessful run-away action.</summary>
    RunAway,

    /// <summary>Random recovery completion delay.</summary>
    AutoRecovery,

    /// <summary>Two-second blast completion delay.</summary>
    BlastTwoSeconds,

    /// <summary>Two-and-a-half-second blast completion delay.</summary>
    BlastTwoAndHalfSeconds,

    /// <summary>Three-second blast completion delay.</summary>
    BlastThreeSeconds,

    /// <summary>Three-and-a-half-second blast completion delay.</summary>
    BlastThreeAndHalfSeconds,

    /// <summary>Four-second blast completion delay.</summary>
    BlastFourSeconds,

    /// <summary>Random status-expiry delay.</summary>
    RandomStatusExpiry,

    /// <summary>Paralysis completion delay.</summary>
    Paralysis,

    /// <summary>Confusion completion delay.</summary>
    Confusion,

    /// <summary>Sleep completion delay.</summary>
    Sleep,

    /// <summary>Random boost-expiry delay.</summary>
    BoostExpiry,
}
