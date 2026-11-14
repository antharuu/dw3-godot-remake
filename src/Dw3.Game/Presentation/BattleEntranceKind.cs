namespace Dw3Production.Presentation;

/// <summary>Why a fighter enters, which selects its source caller, messages and idle.</summary>
internal enum BattleEntranceKind
{
    /// <summary>No entrance: the change is a same-slot form change or the battle start.</summary>
    None,

    /// <summary>The player switched partners, including the forced switch after a knockout.</summary>
    PlayerSwitch,

    /// <summary>The incoming partner of a pair technique tags in after the technique.</summary>
    PlayerTagIn,

    /// <summary>The enemy switched fighters on its own turn.</summary>
    EnemySwitch,

    /// <summary>The next enemy replaces a knocked-out one.</summary>
    EnemyReplacement,

    /// <summary>The active partner's blast form expired and the partner returned to its pre-blast form on the same slot.</summary>
    PlayerBlastReturn,

    /// <summary>An enemy technique forced the active partner back to its base form on the same slot.</summary>
    PlayerDedigivolve,
}
