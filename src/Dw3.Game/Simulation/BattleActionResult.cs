namespace Dw3Production.Simulation;

/// <summary>
/// The result message of a finished action, a child of the action in the source: its pages shown one after
/// the other in the battle message box, each revealed line by line and closed by Cross. The action ends
/// when the last page closes.
/// </summary>
public sealed class BattleActionResult
{
    private readonly IReadOnlyList<BattleActionResultPage> _pages;
    private readonly Action<BattleActionResultPage> _show;
    private int _page;

    /// <summary>Initializes a new instance of the <see cref="BattleActionResult"/> class and shows its first page.</summary>
    /// <param name="pages">Pages in display order, at least one.</param>
    /// <param name="show">Puts a page's text into the battle message box.</param>
    /// <exception cref="ArgumentException">There is no page.</exception>
    public BattleActionResult(IReadOnlyList<BattleActionResultPage> pages, Action<BattleActionResultPage> show)
    {
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(show);
        if (pages.Count == 0)
        {
            throw new ArgumentException("A result message needs a page.", nameof(pages));
        }

        _pages = pages;
        _show = show;
        Reveal = new BattleMessageReveal(2);
        show(pages[0]);
    }

    /// <summary>Gets the page currently shown.</summary>
    public BattleActionResultPage Page => _pages[_page];

    /// <summary>Gets the line reveal of the current page.</summary>
    public BattleMessageReveal Reveal { get; private set; }

    /// <summary>Gets a value indicating whether the last page was closed.</summary>
    public bool IsComplete { get; private set; }

    /// <summary>Builds the result message of a finished action and shows its first page.</summary>
    /// <param name="context">Frozen context of the action.</param>
    /// <param name="show">Puts a page's text into the battle message box.</param>
    /// <returns>The result message, or null when the action has no result to show.</returns>
    public static BattleActionResult? Create(ActionSequenceContext context, Action<BattleActionResultPage> show)
    {
        IReadOnlyList<BattleActionResultPage> pages = BattleActionResultPages.Build(context);
        return pages.Count == 0 ? null : new BattleActionResult(pages, show);
    }

    /// <summary>Advances one source frame of the current page, showing the next page once it closed.</summary>
    /// <param name="confirmPressed">Whether Cross was newly pressed this frame.</param>
    public void Step(bool confirmPressed)
    {
        if (IsComplete)
        {
            return;
        }

        Reveal.Step(confirmPressed);
        if (!Reveal.IsClosed)
        {
            return;
        }

        if (_page + 1 >= _pages.Count)
        {
            IsComplete = true;
            return;
        }

        _page++;
        Reveal = new BattleMessageReveal(2);
        _show(_pages[_page]);
    }
}
