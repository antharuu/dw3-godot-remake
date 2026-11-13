using Dw3Production.Session;

namespace Dw3Production.Simulation;

/// <summary>Resolves encounter-reward equipment conditions from the persistent party state.</summary>
public static class BattleRewardEquipment
{
    /// <summary>The source equipment item that adds one fifth to its wearer's experience reward.</summary>
    public const int ExperienceBonusItem = 0x141;

    /// <summary>The source equipment item that adds one fifth to an encounter's money reward.</summary>
    public const int MoneyBonusItem = 0x142;

    /// <summary>Tests whether the partner active when battle ended wears the money bonus equipment.</summary>
    /// <param name="session">Persistent party state, or null when no session was loaded.</param>
    /// <param name="partyIndex">The source party index active when battle ended.</param>
    /// <returns>True when either source equipment slot contains item 0x142.</returns>
    public static bool HasMoneyBonus(PlayerSession? session, int partyIndex) =>
        partyIndex >= 0
        && partyIndex < session?.Partners.Length
        && session.Partners[partyIndex]?.EquipmentIds.Contains(MoneyBonusItem) == true;

    /// <summary>Tests whether a party partner wears the source experience bonus equipment.</summary>
    /// <param name="session">Persistent party state, or null when no session was loaded.</param>
    /// <param name="partyIndex">The source party index receiving experience.</param>
    /// <returns>True when either source equipment slot contains item 0x141.</returns>
    public static bool HasExperienceBonus(PlayerSession? session, int partyIndex) =>
        partyIndex >= 0
        && partyIndex < session?.Partners.Length
        && session.Partners[partyIndex]?.EquipmentIds.Contains(ExperienceBonusItem) == true;
}
