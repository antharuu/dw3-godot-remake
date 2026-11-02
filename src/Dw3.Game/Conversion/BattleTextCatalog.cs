namespace Dw3Production.Conversion;

/// <summary>Builds render lines from the disc-decoded battle text tables. Text-window
/// command 5 selects a substitution by its one-based argument, matching text-window behavior.</summary>
public sealed class BattleTextCatalog
{
    private readonly BattleTextData _data;

    /// <summary>Initializes a new instance of the <see cref="BattleTextCatalog"/> class with the given text data.</summary>
    /// <param name="data">The battle text tables decoded from the disc.</param>
    public BattleTextCatalog(BattleTextData data) => _data = data;

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

    /// <summary>Wraps an arbitrary token stream in a <see cref="BattleTextLine"/> with a debug label.</summary>
    /// <param name="tokens">The token stream to wrap.</param>
    /// <param name="debug">The debug description for probe logs.</param>
    /// <returns>A new <see cref="BattleTextLine"/> with no substitutions.</returns>
    public static BattleTextLine Tokens(IReadOnlyList<TextToken> tokens, string debug) => new(tokens, [], debug);

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

        var type = key switch { "technique" => 3, "digivolve" => 12, "item" => 14, "won" or "lost" => 1, _ => 2 };
        if (type == 1)
        {
            return FromMenu(template.Entry, $"MFIGHT entry={template.Entry} message={key}");
        }

        var actor = substitutions.Length > 0 ? substitutions[0] : [];
        var action = substitutions.Length > 1 ? substitutions[1..] : [];
        var second = new BattleTextLine(_data.Menu[template.Entry], action, $"MFIGHT entry={template.Entry} message={key}");
        return new BattleTextLine(_data.Menu[0x16], [actor], $"MFIGHT entry=22 message={key}", second, type);
    }

    /// <summary>Converts an integer to a sequence of glyph tokens using digit codes 4–13.</summary>
    /// <param name="value">The integer to convert.</param>
    /// <returns>A read-only list of glyph tokens representing the decimal digits.</returns>
    public static IReadOnlyList<TextToken> Number(int value)
    {
        return [.. value.ToString(System.Globalization.CultureInfo.InvariantCulture).Select(character => new TextToken("g", 4 + character - '0', 0, []))];
    }

    private BattleTextLine FromMenu(int entry, string debug) => From(_data.Menu, entry, debug);

    private static BattleTextLine From(IReadOnlyList<IReadOnlyList<TextToken>> table, int entry, string debug) =>
        entry >= 0 && entry < table.Count ? new BattleTextLine(table[entry], [], debug) : BattleTextLine.Missing;
}
