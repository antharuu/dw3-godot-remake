using Dw3Production.Conversion;

namespace Dw3Production.Presentation;

/// <summary>
/// The messages each source caller shows around an entrance. A partner switch calls the outgoing partner back
/// (MFIGHT 0x38) and names the incoming one after (0x39), a pair technique's tag-in only names it after, a voluntary
/// enemy switch announces the switch (0x4D) and every enemy entrance names the incoming enemy after (type 13).
/// A blast return names the returned form after (0x62); a forced dedigivolve names the returned form after (0x47).
/// Both return entrances have no before message.
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
        string? afterKey = kind switch
        {
            BattleEntranceKind.PlayerBlastReturn => "blastReturn",
            BattleEntranceKind.PlayerDedigivolve => "dedigivolve",
            _ => null,
        };
        BattleTextLine afterLine;
        if (afterKey is not null)
        {
            afterLine = text?.Message(afterKey, incomingName) ?? BattleTextLine.Missing;
        }
        else if (enemy)
        {
            afterLine = text?.EnemyEntered(incomingName) ?? BattleTextLine.Missing;
        }
        else
        {
            afterLine = text?.Message("switchIn", incomingName) ?? BattleTextLine.Missing;
        }

        var after = new BattleEntranceMessage(afterLine, 2);
        BattleEntranceMessage? before = kind switch
        {
            BattleEntranceKind.PlayerSwitch => new(text?.Message("switch", outgoingName) ?? BattleTextLine.Missing, 2),
            BattleEntranceKind.EnemySwitch => new(text?.Message("enemySwitch") ?? BattleTextLine.Missing, 1),
            _ => null,
        };
        return (before, after);
    }
}
