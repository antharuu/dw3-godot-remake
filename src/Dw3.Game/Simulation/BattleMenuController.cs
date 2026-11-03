using Dw3Production.Conversion;

namespace Dw3Production.Simulation;

/// <summary>
/// The battle menu state machine. Technique and Item use the source's discrete L1/R1 pages,
/// not a fabricated vertical scrolling list. A page turn resets the cursor to row zero.
/// </summary>
public sealed class BattleMenuController
{
    /// <summary>Gets the menu currently open. A fresh controller starts on the root menu.</summary>
    public BattleMenuKind Menu { get; private set; } = BattleMenuKind.Root;

    /// <summary>Gets the cursor row inside the current visible page, 0..<see cref="PageSize"/> - 1.</summary>
    public int Cursor { get; private set; }

    /// <summary>Gets the index in <see cref="Lines"/> of the current page's first row, always a multiple of <see cref="PageSize"/>.</summary>
    public int First { get; private set; }

    /// <summary>Gets the number of rows per page: 6 for techniques, 7 for items, otherwise every line on one page.</summary>
    public int PageSize { get; private set; }

    /// <summary>Gets the number of pages needed to show every line, 0 when the menu is empty.</summary>
    public int PageCount => Lines.Count == 0 ? 0 : (Lines.Count + PageSize - 1) / PageSize;

    /// <summary>Gets the index in <see cref="Lines"/> under the cursor. It can reach past the end only when the menu is empty.</summary>
    public int SelectedIndex => First + Cursor;

    /// <summary>Gets every line of the open menu across all pages, in display order.</summary>
    public IReadOnlyList<BattleMenuLine> Lines { get; private set; } = [];

    /// <summary>Gets the visible source page and relative cursor for presentation.</summary>
    public BattleMenuView View => new(Menu, Cursor, First, PageSize, PageCount,
        [.. Lines.Skip(First).Take(PageSize)], PreviousPageText, NextPageText, StatusText);

    /// <summary>Gets or sets the label drawn next to the previous page hint (L1), null for menus without pages.</summary>
    private BattleTextLine? PreviousPageText { get; set; }

    /// <summary>Gets or sets the label drawn next to the next page hint (R1), null for menus without pages.</summary>
    private BattleTextLine? NextPageText { get; set; }

    /// <summary>Gets or sets the status block shown beside the menu, such as the MP of the active partner. Null when the menu has none.</summary>
    private BattleMenuStatusText? StatusText { get; set; }

    /// <summary>Opens a menu (or re-opens the root), on its first source page.</summary>
    /// <param name="menu">Menu to open, which sets the page size.</param>
    /// <param name="lines">Every line of the menu, across all pages.</param>
    /// <param name="cursor">Initial row, clamped to the first page's populated rows.</param>
    /// <param name="previousPageText">Label of the L1 page hint, or null.</param>
    /// <param name="nextPageText">Label of the R1 page hint, or null.</param>
    /// <param name="statusText">Status block shown beside the menu, or null.</param>
    public void Open(BattleMenuKind menu, IReadOnlyList<BattleMenuLine> lines, int cursor = 0,
        BattleTextLine? previousPageText = null, BattleTextLine? nextPageText = null, BattleMenuStatusText? statusText = null)
    {
        Menu = menu;
        Lines = lines;
        PreviousPageText = previousPageText;
        NextPageText = nextPageText;
        StatusText = statusText;
        PageSize = menu switch
        {
            BattleMenuKind.Technique => 6, // rows per technique page
            BattleMenuKind.Item => 7,      // rows per item page
            _ => Math.Max(1, lines.Count),
        };
        First = 0;
        Cursor = lines.Count == 0 ? 0 : Math.Clamp(cursor, 0, Math.Min(lines.Count, PageSize) - 1);
    }

    /// <summary>Opens (or returns to) the six-command root menu.</summary>
    /// <param name="lines">The six command lines, in <see cref="BattleCommand"/> order.</param>
    /// <param name="cursor">Row to put the cursor on, for example the last command used.</param>
    public void OpenRoot(IReadOnlyList<BattleMenuLine> lines, int cursor = 0) => Open(BattleMenuKind.Root, lines, cursor);

    /// <summary>Moves the cursor up, clamped to the current source page.</summary>
    /// <returns><see langword="true"/> when the cursor moved, <see langword="false"/> on the first row, so the caller plays the cursor sound only on a move.</returns>
    public bool MoveUp()
    {
        if (Cursor <= 0)
        {
            return false;
        }

        Cursor--;
        return true;
    }

    /// <summary>Moves the cursor down, clamped to the current page's populated rows.</summary>
    /// <returns><see langword="true"/> when the cursor moved, <see langword="false"/> on the last populated row.</returns>
    public bool MoveDown()
    {
        if (Cursor >= VisibleCount - 1)
        {
            return false;
        }

        Cursor++;
        return true;
    }

    /// <summary>Shows the previous page of the Technique or Item menu (L1) with the cursor on its first row. Other menus ignore it.</summary>
    /// <returns><see langword="true"/> when the page changed, <see langword="false"/> on the first page or in a menu without pages.</returns>
    public bool PreviousPage() => SetPage(CurrentPage - 1);

    /// <summary>Shows the next page of the Technique or Item menu (R1) with the cursor on its first row. Other menus ignore it.</summary>
    /// <returns><see langword="true"/> when the page changed, <see langword="false"/> on the last page or in a menu without pages.</returns>
    public bool NextPage() => SetPage(CurrentPage + 1);

    /// <summary>Gets a value indicating whether the line under the cursor exists and is enabled, so Cross on a greyed line does nothing.</summary>
    public bool CanConfirm => Lines.Count > 0 && SelectedIndex < Lines.Count && Lines[SelectedIndex].Enabled;

    /// <summary>Gets the zero-based index of the page shown.</summary>
    private int CurrentPage => First / PageSize;

    /// <summary>Gets the number of populated rows on the page shown, smaller than <see cref="PageSize"/> on a last partial page.</summary>
    private int VisibleCount => Math.Min(PageSize, Math.Max(0, Lines.Count - First));

    /// <summary>Moves to a page of a paginated menu, clamped to the existing pages, and resets the cursor to row 0 as the original menu does.</summary>
    /// <param name="page">Wanted page index, may be out of range.</param>
    /// <returns><see langword="true"/> when the shown page changed.</returns>
    private bool SetPage(int page)
    {
        if (Menu is not (BattleMenuKind.Technique or BattleMenuKind.Item) || PageCount == 0)
        {
            return false;
        }

        var clamped = Math.Clamp(page, 0, PageCount - 1);
        if (clamped == CurrentPage)
        {
            return false;
        }

        First = clamped * PageSize;
        Cursor = 0;
        return true;
    }
}
