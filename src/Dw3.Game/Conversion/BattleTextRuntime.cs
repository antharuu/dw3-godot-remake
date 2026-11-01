namespace Dw3Production.Conversion;

/// <summary>One renderable battle text line. Tokens remain source-font tokens; Debug is only an
/// auditable description for probe logs and is never a player-facing substitute.</summary>
public sealed record BattleTextLine(IReadOnlyList<TextToken> Tokens, IReadOnlyList<IReadOnlyList<TextToken>> Substitutions, string Debug, BattleTextLine? Second = null, int Type = 1)
{
    public static readonly BattleTextLine Missing = new([], [], "[UNSOURCED battle text]");
}

/// <summary>Builds render lines from the disc-decoded battle text tables. Text-window
/// command 5 selects a substitution by its one-based argument, matching text-window behavior.</summary>
public sealed class BattleTextCatalog
{
    private readonly BattleTextData _data;

    public BattleTextCatalog(BattleTextData data) => _data = data;

    public BattleTextLine Command(int command) => command >= 0 && command < _data.Commands.Count
        ? FromMenu(_data.Commands[command].Entry, $"MFIGHT command={command}")
        : BattleTextLine.Missing;

    /// <summary>Resolves DIGNAM by the source nameId, never by fighter id.</summary>
    public BattleTextLine DigimonByNameId(int nameId) => From(_data.DigimonNames, nameId, $"DIGNAM nameId={nameId}");
    public BattleTextLine Technique(int id) => From(_data.TechniqueNames, id, $"SKLNAM id={id}");
    public BattleTextLine SkillInfo(int id) => From(_data.SkillInfo, id, $"SKLINF id={id}");
    public BattleTextLine ItemInfo(int id) => From(_data.ItemInfo, id, $"ITMINF id={id}");
    public BattleTextLine MenuEntry(int entry) => FromMenu(entry, $"MFIGHT entry={entry}");
    public BattleTextLine Report(string key) => _data.ReportTemplates.TryGetValue(key, out var entry)
        ? From(_data.Report, entry, $"FGTRPT {key}") : BattleTextLine.Missing;

    public BattleTextLine Report(string key, params IReadOnlyList<TextToken>[] substitutions) => _data.ReportTemplates.TryGetValue(key, out var entry)
        ? new BattleTextLine(_data.Report[entry], substitutions, $"FGTRPT {key}") : BattleTextLine.Missing;

    public BattleTextLine Tokens(IReadOnlyList<TextToken> tokens, string debug) => new(tokens, [], debug);

    public BattleTextLine Message(string key, params IReadOnlyList<TextToken>[] substitutions)
    {
        if (!_data.Messages.TryGetValue(key, out var template)) return BattleTextLine.Missing;
        var type = key switch { "technique" => 3, "digivolve" => 12, "item" => 14, "won" or "lost" => 1, _ => 2 };
        if (type == 1) return FromMenu(template.Entry, $"MFIGHT entry={template.Entry} message={key}");
        var actor = substitutions.Length > 0 ? substitutions[0] : [];
        var action = substitutions.Length > 1 ? substitutions[1..] : [];
        var second = new BattleTextLine(_data.Menu[template.Entry], action, $"MFIGHT entry={template.Entry} message={key}");
        return new BattleTextLine(_data.Menu[0x16], [actor], $"MFIGHT entry=22 message={key}", second, type);
    }

    public static IReadOnlyList<TextToken> Number(int value)
    {
        return value.ToString(System.Globalization.CultureInfo.InvariantCulture)
            .Select(character => new TextToken("g", 4 + character - '0', 0, [])).ToArray();
    }

    private BattleTextLine FromMenu(int entry, string debug) => From(_data.Menu, entry, debug);
    private static BattleTextLine From(IReadOnlyList<IReadOnlyList<TextToken>> table, int entry, string debug) =>
        entry >= 0 && entry < table.Count ? new BattleTextLine(table[entry], [], debug) : BattleTextLine.Missing;
}
