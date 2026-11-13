using Dw3Production.Simulation;
using Xunit;

namespace Dw3.Game.Tests;

/// <summary>Exercises the fight-report's player-confirmed pages with a fabricated settled reward.</summary>
public sealed class FightReportControllerInteractionTests
{
    /// <summary>Each typed page waits for a separate Cross edge, including the item page, before the report may exit.</summary>
    [Fact]
    public void Step_ExpMoneyAndItemPages_RequireSeparateConfirmationEdges()
    {
        var report = new FightReportController(Input());
        var pages = new List<int>();
        var priorArrowPage = -1;

        for (var frame = 0; frame < 4_000 && !report.View.Done; frame++)
        {
            FightReportMessage? message = report.View.Message;
            var crossEdge = false;
            if (message is not null && message.ArrowVisible && message.Entry != priorArrowPage)
            {
                pages.Add(message.Entry);
                priorArrowPage = message.Entry;
                crossEdge = true;
            }

            report.Step(crossEdge);
        }

        Assert.True(report.View.Done);
        Assert.Null(report.View.HeaderExp);
        Assert.Equal([2, 10, 11], pages);
    }

    /// <summary>A message typed through a resolved-text counter reveals every glyph of its own substitutions before the arrow waits.</summary>
    [Fact]
    public void Step_WithTokenCounter_TypesEveryResolvedGlyphBeforeTheArrow()
    {
        var requests = new List<(int Entry, int[] Values)>();
        var report = new FightReportController(
            Input() with { MessageTokenCounts = null },
            (entry, values) =>
            {
                requests.Add((entry, [.. values]));
                return entry == 2 ? 14 : 9;
            });
        var started = -1;
        var frame = 0;
        for (; frame < 4_000 && report.View.Message?.ArrowVisible != true; frame++)
        {
            if (started < 0 && report.View.Message?.Entry == 2)
            {
                started = frame;
            }

            report.Step(false);
        }

        FightReportMessage message = Assert.IsType<FightReportMessage>(report.View.Message);
        Assert.Equal(2, message.Entry);
        Assert.Equal(14, message.TokenCount);
        Assert.Equal(14, message.TokensRevealed);
        Assert.Equal(1, report.View.HeaderExp);
        Assert.True(frame - started >= 14 * 7, $"14 glyphs typed with delay 6 arrived after {frame - started} frames");
        Assert.Equal(2, requests[0].Entry);
        Assert.Equal([1], requests[0].Values);
    }

    /// <summary>Builds a reward with no partner or form level-up pages and one genuine item page.</summary>
    /// <returns>Settled report input whose money and item pages must be explicitly confirmed.</returns>
    private static FightReportInput Input() => new(
        [new FightReportPartner(0, 0, 1, 1, 0, 1, 1, [])],
        Money: 50,
        Item: 6,
        MessageTokenCounts: new Dictionary<int, int> { [2] = 2, [10] = 2, [11] = 2 });
}
