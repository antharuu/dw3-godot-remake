using Dw3Production.Conversion;
using Dw3Production.Presentation;
using Dw3Production.Simulation;
using Xunit;

namespace Dw3.Game.Tests;

/// <summary>Verifies that every report message resolves through extracted report text and its matching extracted name table.</summary>
public sealed class FightReportMessageLineResolverTests
{
    /// <summary>Resolves every report-page entry without falling back to a debug or unrelated report template.</summary>
    [Fact]
    public void Resolve_AllReportEntries_UsesExtractedTemplateAndExpectedSubstitutions()
    {
        BattleTextCatalog catalog = CreateCatalog();
        var resolver = new FightReportMessageLineResolver(catalog, FighterName, ItemName);
        var expected = new Dictionary<int, string>
        {
            [2] = "FGTRPT expGained",
            [4] = "FGTRPT levelGained",
            [5] = "FGTRPT skillLearned",
            [6] = "FGTRPT skillKnown",
            [7] = "FGTRPT partnerLevel",
            [8] = "FGTRPT digimonLearned",
            [9] = "FGTRPT slotsFull",
            [10] = "FGTRPT moneyGained",
            [11] = "FGTRPT itemGained",
        };

        foreach ((int entry, string debug) in expected)
        {
            BattleTextLine line = resolver.Resolve(Message(entry));
            Assert.Equal(debug, line.Debug);
            Assert.NotSame(BattleTextLine.Missing, line);
        }

        BattleTextLine level = resolver.Resolve(Message(4));
        BattleTextLine skill = resolver.Resolve(Message(5));
        BattleTextLine learned = resolver.Resolve(Message(8));
        BattleTextLine item = resolver.Resolve(Message(11));

        Assert.Empty(level.Substitutions);
        Assert.Equal(catalog.Technique(42).Tokens, skill.Substitutions[0]);
        Assert.Equal(catalog.DigimonByNameId(4).Tokens, learned.Substitutions[0]);
        Assert.Equal(ItemName(6).Tokens, item.Substitutions[0]);
        Assert.Equal("FSITMNAM item=6", item.Substitutions[0][0].Type);
    }

    /// <summary>Builds an entry-specific report message with stable fabricated substitution values.</summary>
    /// <param name="entry">Fight-report message entry.</param>
    /// <returns>A message accepted by the resolver.</returns>
    private static FightReportMessage Message(int entry) => entry switch
    {
        4 => new FightReportMessage(entry, [], 1, 1, false, 0),
        7 => new FightReportMessage(entry, [17, 3], 1, 1, false, 0),
        5 or 6 => new FightReportMessage(entry, [42], 1, 1, false, 0),
        8 => new FightReportMessage(entry, [4], 1, 1, false, 0),
        11 => new FightReportMessage(entry, [6], 1, 1, false, 0),
        _ => new FightReportMessage(entry, [77], 1, 1, false, 0),
    };

    /// <summary>Builds a complete fabricated extracted-text catalog.</summary>
    /// <returns>Catalog with distinct report and lookup tables.</returns>
    private static BattleTextCatalog CreateCatalog()
    {
        var table = new List<IReadOnlyList<TextToken>>();
        for (var index = 0; index < 50; index++)
        {
            table.Add([new TextToken("g", index + 4, 0, [])]);
        }

        IReadOnlyList<(string Key, int Entry)> reportEntries =
        [
            ("digimonLearned", 8),
            ("expGained", 2),
            ("itemGained", 11),
            ("levelGained", 4),
            ("moneyGained", 10),
            ("partnerLevel", 7),
            ("skillKnown", 6),
            ("skillLearned", 5),
            ("slotsFull", 9),
        ];
        IReadOnlyDictionary<string, int> templates = reportEntries.ToDictionary(entry => entry.Key, entry => entry.Entry);
        IReadOnlyDictionary<string, BattleTextTemplate> messages = Enumerable.Empty<KeyValuePair<string, BattleTextTemplate>>()
            .ToDictionary(entry => entry.Key, entry => entry.Value);
        var data = new BattleTextData(table, table, table, table, table, table, [], messages, templates);
        return new BattleTextCatalog(data);
    }

    /// <summary>Builds a fabricated resolved fighter-name line.</summary>
    /// <param name="id">Fighter identifier.</param>
    /// <returns>Named token line.</returns>
    private static BattleTextLine FighterName(int id) => BattleTextCatalog.Tokens([], $"fighter={id}");

    /// <summary>Builds a fabricated extracted item-name line distinct from item descriptions.</summary>
    /// <param name="id">Item identifier.</param>
    /// <returns>Named token line from the FSITMNAM source table.</returns>
    private static BattleTextLine ItemName(int id) => BattleTextCatalog.Tokens([new TextToken($"FSITMNAM item={id}", id, 0, [])], $"FSITMNAM item={id}");
}
