using Dw3Production.Simulation;

namespace Dw3Production.Conversion;

/// <summary>Builds render lines from the disc-decoded battle text tables. Text-window
/// command 5 selects a substitution by its one-based argument, matching text-window behavior.</summary>
public sealed class BattleTextCatalog
{
    /// <summary>MFIGHT entry before the fighter-list lines: entry base + count names that many fighters.</summary>
    private const int FightersLineBase = 0x15;

    /// <summary>Largest fighter count a fighter-list line names.</summary>
    private const int MaxNamedFighters = 3;

    private readonly BattleTextData _data;

    /// <summary>Initializes a new instance of the <see cref="BattleTextCatalog"/> class with the given text data.</summary>
    /// <param name="data">The battle text tables decoded from the disc.</param>
    public BattleTextCatalog(BattleTextData data) => _data = data;

    /// <summary>Wraps an arbitrary token stream in a <see cref="BattleTextLine"/> with a debug label.</summary>
    /// <param name="tokens">The token stream to wrap.</param>
    /// <param name="debug">The debug description for probe logs.</param>
    /// <returns>A new <see cref="BattleTextLine"/> with no substitutions.</returns>
    public static BattleTextLine Tokens(IReadOnlyList<TextToken> tokens, string debug) => new(tokens, [], debug);

    /// <summary>Converts an integer to a sequence of glyph tokens using digit codes 4–13.</summary>
    /// <param name="value">The integer to convert.</param>
    /// <returns>A read-only list of glyph tokens representing the decimal digits.</returns>
    public static IReadOnlyList<TextToken> Number(int value)
    {
        return [.. value.ToString(System.Globalization.CultureInfo.InvariantCulture).Select(character => new TextToken("g", 4 + character - '0', 0, []))];
    }

    /// <summary>Returns the battle text line for a menu command index.</summary>
    /// <param name="command">The zero-based command index in the battle menu.</param>
    /// <returns>The resolved <see cref="BattleTextLine"/>, or <see cref="BattleTextLine.Missing"/>.</returns>
    public BattleTextLine Command(int command) => command >= 0 && command < _data.Commands.Count
        ? FromMenu(_data.Commands[command].Entry, $"MFIGHT command={command}")
        : BattleTextLine.Missing;

    /// <summary>Resolves DIGNAM by the source nameId, never by fighter id.</summary>
    /// <param name="nameId">The source DIGNAM nameId.</param>
    /// <returns>The resolved <see cref="BattleTextLine"/>, or <see cref="BattleTextLine.Missing"/>.</returns>
    public BattleTextLine DigimonByNameId(int nameId) => From(_data.DigimonNames, nameId, $"DIGNAM nameId={nameId}");

    /// <summary>Returns the technique name line for the given technique id.</summary>
    /// <param name="id">The technique id.</param>
    /// <returns>The resolved <see cref="BattleTextLine"/>.</returns>
    public BattleTextLine Technique(int id) => From(_data.TechniqueNames, id, $"SKLNAM id={id}");

    /// <summary>Returns the skill info line for the given id.</summary>
    /// <param name="id">The skill info index.</param>
    /// <returns>The resolved <see cref="BattleTextLine"/>.</returns>
    public BattleTextLine SkillInfo(int id) => From(_data.SkillInfo, id, $"SKLINF id={id}");

    /// <summary>Returns the item info line for the given id.</summary>
    /// <param name="id">The item info index.</param>
    /// <returns>The resolved <see cref="BattleTextLine"/>.</returns>
    public BattleTextLine ItemInfo(int id) => From(_data.ItemInfo, id, $"ITMINF id={id}");

    /// <summary>Returns a raw MFIGHT entry line by its zero-based entry index.</summary>
    /// <param name="entry">The MFIGHT entry index.</param>
    /// <returns>The resolved <see cref="BattleTextLine"/>.</returns>
    public BattleTextLine MenuEntry(int entry) => FromMenu(entry, $"MFIGHT entry={entry}");

    /// <summary>Returns the battle report line for the given key.</summary>
    /// <param name="key">The report template key (e.g. "expGained").</param>
    /// <returns>The resolved <see cref="BattleTextLine"/>.</returns>
    public BattleTextLine Report(string key) => _data.ReportTemplates.TryGetValue(key, out var entry)
        ? From(_data.Report, entry, $"FGTRPT {key}") : BattleTextLine.Missing;

    /// <summary>Returns the battle report line with substitutions for the given key.</summary>
    /// <param name="key">The report template key.</param>
    /// <param name="substitutions">The token streams to substitute into the template.</param>
    /// <returns>The resolved <see cref="BattleTextLine"/>.</returns>
    public BattleTextLine Report(string key, params IReadOnlyList<TextToken>[] substitutions) => _data.ReportTemplates.TryGetValue(key, out var entry)
        ? new BattleTextLine(_data.Report[entry], substitutions, $"FGTRPT {key}") : BattleTextLine.Missing;

    /// <summary>Builds a two-part battle message line for the given key and substitutions.</summary>
    /// <param name="key">The message template key (e.g. "technique", "digivolve").</param>
    /// <param name="substitutions">The actor and action substitutions.</param>
    /// <returns>The resolved <see cref="BattleTextLine"/>.</returns>
    public BattleTextLine Message(string key, params IReadOnlyList<TextToken>[] substitutions)
    {
        if (!_data.Messages.TryGetValue(key, out var template))
        {
            return BattleTextLine.Missing;
        }

        var type = key switch { "technique" => 3, "damage" => 4, "recovered" => 8, "multiHitDamage" => 16, "digivolve" => 12, "item" => 14, "won" or "lost" => 1, _ => 2 };
        if (type == 1)
        {
            return FromMenu(template.Entry, $"MFIGHT entry={template.Entry} message={key}");
        }

        var actor = substitutions.Length > 0 ? substitutions[0] : [];
        var action = substitutions.Length > 1 ? substitutions[1..] : [];
        var second = new BattleTextLine(_data.Menu[template.Entry], action, $"MFIGHT entry={template.Entry} message={key}");
        return new BattleTextLine(_data.Menu[0x16], [actor], $"MFIGHT entry=22 message={key}", second, type);
    }

    /// <summary>
    /// Builds the message of one action result page: a side heal names each hurt fighter through the fighter-list
    /// line, every other page names its target before the key's line and numbers.
    /// </summary>
    /// <param name="page">Result page.</param>
    /// <param name="name">Resolves a fighter id to its displayed name.</param>
    /// <returns>The resolved line, or <see cref="BattleTextLine.Missing"/>.</returns>
    public BattleTextLine ResultPage(BattleActionResultPage page, Func<int, IReadOnlyList<TextToken>> name)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(name);
        IReadOnlyList<TextToken>[] numbers = [.. page.Numbers.Select(Number)];
        return page.Key == BattleActionResultPages.SideRecoveredKey
            ? FightersMessage(page.Key, [.. page.NamedIds.Select(name)], numbers)
            : Message(page.Key, [name(page.TargetId), .. numbers]);
    }

    /// <summary>
    /// Builds a message whose first line names up to three fighters through the fighter-list entry for their count
    /// (source message type 9), and whose second line is the key's template with its values.
    /// </summary>
    /// <param name="key">The message template key of the second line, for example <c>sideRecovered</c>.</param>
    /// <param name="names">Name token streams of the named fighters, in slot order.</param>
    /// <param name="values">Substitutions of the second line.</param>
    /// <returns>The resolved line, or <see cref="BattleTextLine.Missing"/> for an unknown key or fighter count.</returns>
    public BattleTextLine FightersMessage(string key, IReadOnlyList<IReadOnlyList<TextToken>> names, params IReadOnlyList<TextToken>[] values)
    {
        ArgumentNullException.ThrowIfNull(names);
        if (!_data.Messages.TryGetValue(key, out var template) || names.Count is < 1 or > MaxNamedFighters)
        {
            return BattleTextLine.Missing;
        }

        int entry = FightersLineBase + names.Count;
        var second = new BattleTextLine(_data.Menu[template.Entry], values, $"MFIGHT entry={template.Entry} message={key}");
        return new BattleTextLine(_data.Menu[entry], names, $"MFIGHT entry={entry} message={key} names={names.Count}", second, 9);
    }

    /// <summary>Looks up one entry of a text table without throwing, so a pack with a short table still renders the rest of the battle.</summary>
    /// <param name="table">Token lines of a text table.</param>
    /// <param name="entry">Entry index. Any value outside 0..Count-1 is accepted.</param>
    /// <param name="debug">Label recorded on the line for diagnostics.</param>
    /// <returns>The line without substitutions, or <see cref="BattleTextLine.Missing"/> when the index is out of range.</returns>
    private static BattleTextLine From(IReadOnlyList<IReadOnlyList<TextToken>> table, int entry, string debug) =>
        entry >= 0 && entry < table.Count ? new BattleTextLine(table[entry], [], debug) : BattleTextLine.Missing;

    /// <summary>Looks up a line of the battle menu text table, tagging it with a debug label for the probe logs.</summary>
    /// <param name="entry">Entry index in the menu table.</param>
    /// <param name="debug">Label recorded on the line for diagnostics.</param>
    /// <returns>The line, or <see cref="BattleTextLine.Missing"/> when the index is out of range.</returns>
    private BattleTextLine FromMenu(int entry, string debug) => From(_data.Menu, entry, debug);
}
