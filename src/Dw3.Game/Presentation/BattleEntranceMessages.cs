using Dw3Production.Conversion;

namespace Dw3Production.Presentation;

/// <summary>
/// The messages each source caller shows around an entrance. A partner switch calls the outgoing partner back
/// (MFIGHT 0x38) and names the incoming one after (0x39), a pair technique's tag-in only names it after, a voluntary
/// enemy switch announces the switch (0x4D) and every enemy entrance names the incoming enemy after (type 13).
/// </summary>
internal static class BattleEntranceMessages
{
    /// <summary>Builds the caller's messages of one entrance.</summary>
    /// <param name="kind">Entrance kind.</param>
    /// <param name="text">Battle text catalogue, or <see langword="null"/> when no text is loaded.</param>
    /// <param name="outgoingName">Name tokens of the outgoing fighter.</param>
    /// <param name="incomingName">Name tokens of the incoming fighter.</param>
    /// <returns>The optional message before the entrance and the message after it.</returns>
    public static (BattleEntranceMessage? Before, BattleEntranceMessage After) For(
        BattleEntranceKind kind,
        BattleTextCatalog? text,
        IReadOnlyList<TextToken> outgoingName,
        IReadOnlyList<TextToken> incomingName)
    {
        bool enemy = kind is BattleEntranceKind.EnemySwitch or BattleEntranceKind.EnemyReplacement;
        var after = new BattleEntranceMessage(
            (enemy ? text?.EnemyEntered(incomingName) : text?.Message("switchIn", incomingName)) ?? BattleTextLine.Missing,
            2);
        BattleEntranceMessage? before = kind switch
        {
            BattleEntranceKind.PlayerSwitch => new(text?.Message("switch", outgoingName) ?? BattleTextLine.Missing, 2),
            BattleEntranceKind.EnemySwitch => new(text?.Message("enemySwitch") ?? BattleTextLine.Missing, 1),
            _ => null,
        };
        return (before, after);
    }
}
