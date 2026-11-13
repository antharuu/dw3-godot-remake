using Dw3Production.Conversion;
using Dw3Production.Simulation;

namespace Dw3Production.Presentation;

/// <summary>Resolves every fight-report message entry through its extracted text table and typed substitutions.</summary>
public sealed class FightReportMessageLineResolver
{
    private readonly BattleTextCatalog _catalog;
    private readonly Func<int, BattleTextLine> _fighterName;

    private readonly Func<int, BattleTextLine> _itemName;

    /// <summary>Initializes a new instance of the <see cref="FightReportMessageLineResolver"/> class.</summary>
    /// <param name="catalog">Extracted battle text catalog.</param>
    /// <param name="fighterName">Resolver for fighter identifiers used by partner and form messages.</param>
    /// <param name="itemName">Resolver for item identifiers used by item-reward messages.</param>
    public FightReportMessageLineResolver(BattleTextCatalog catalog, Func<int, BattleTextLine> fighterName, Func<int, BattleTextLine> itemName)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _fighterName = fighterName ?? throw new ArgumentNullException(nameof(fighterName));
        _itemName = itemName ?? throw new ArgumentNullException(nameof(itemName));
    }

    /// <summary>Resolves a visible report message into extracted text and its source-specific substitutions.</summary>
    /// <param name="message">The controller message currently shown by the report.</param>
    /// <returns>Extracted report text ready for the PSX painter.</returns>
    /// <exception cref="InvalidOperationException">Thrown when a typed report page lacks required substitutions.</exception>
    public BattleTextLine Resolve(FightReportMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return message.Entry switch
        {
            2 => _catalog.Report("expGained", Number(message, 0)),
            4 => _catalog.Report("levelGained"),
            5 => _catalog.Report("skillLearned", Technique(message, 0).Tokens),
            6 => _catalog.Report("skillKnown", Technique(message, 0).Tokens),
            7 => _catalog.Report("partnerLevel", Fighter(message, 0).Tokens, Number(message, 1)),
            8 => _catalog.Report("digimonLearned", DigimonName(message, 0).Tokens),
            9 => _catalog.Report("slotsFull"),
            10 => _catalog.Report("moneyGained", Number(message, 0)),
            11 => _catalog.Report("itemGained", Item(message, 0).Tokens),
            _ => throw new InvalidOperationException($"unsupported fight-report entry={message.Entry}"),
        };
    }

    /// <summary>Counts the glyphs the report typewriter reveals for a message resolved with its own substitutions.</summary>
    /// <param name="entry">Report message entry.</param>
    /// <param name="substitutions">Values the controller substitutes into the message.</param>
    /// <returns>The typed-glyph count of the resolved line, 0 when the text is not extracted.</returns>
    public int CountGlyphs(int entry, IReadOnlyList<int> substitutions) =>
        BattleTextGlyphCount.Count(Resolve(new FightReportMessage(entry, substitutions, 0, 0, false, 0)));

    /// <summary>Converts a numeric report substitution into extracted font tokens.</summary>
    /// <param name="message">Message containing the numeric value.</param>
    /// <param name="index">Substitution index.</param>
    /// <returns>Decimal glyph tokens.</returns>
    private static IReadOnlyList<TextToken> Number(FightReportMessage message, int index) => BattleTextCatalog.Number(Value(message, index));

    /// <summary>Reads a required typed substitution from the controller message.</summary>
    /// <param name="message">Message providing substitutions.</param>
    /// <param name="index">Required substitution index.</param>
    /// <returns>The source identifier or numeric value.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the controller did not provide the required value.</exception>
    private static int Value(FightReportMessage message, int index) => index < message.Substitutions.Count
        ? message.Substitutions[index]
        : throw new InvalidOperationException($"fight-report entry={message.Entry} substitution={index} missing");

    /// <summary>Resolves a fighter identifier into its extracted DIGNAM line.</summary>
    /// <param name="message">Message containing the fighter identifier.</param>
    /// <param name="index">Substitution index.</param>
    /// <returns>Resolved fighter-name tokens.</returns>
    private BattleTextLine Fighter(FightReportMessage message, int index) => _fighterName(Value(message, index));

    /// <summary>Resolves a technique identifier through SKLNAM.</summary>
    /// <param name="message">Message containing the technique identifier.</param>
    /// <param name="index">Substitution index.</param>
    /// <returns>Technique-name tokens.</returns>
    private BattleTextLine Technique(FightReportMessage message, int index) => _catalog.Technique(Value(message, index));

    /// <summary>Resolves a source DIGNAM name identifier.</summary>
    /// <param name="message">Message containing the DIGNAM identifier.</param>
    /// <param name="index">Substitution index.</param>
    /// <returns>Digimon-name tokens.</returns>
    private BattleTextLine DigimonName(FightReportMessage message, int index) => _catalog.DigimonByNameId(Value(message, index));

    /// <summary>Resolves an item identifier through the extracted FSITMNAM item-name table.</summary>
    /// <param name="message">Message containing the item identifier.</param>
    /// <param name="index">Substitution index.</param>
    /// <returns>Item-name tokens.</returns>
    private BattleTextLine Item(FightReportMessage message, int index) => _itemName(Value(message, index));
}
