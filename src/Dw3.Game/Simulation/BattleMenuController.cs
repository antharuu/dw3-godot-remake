using Dw3Production.Conversion;

namespace Dw3Production.Simulation;

/// <summary>Which battle menu is open: the six-command root, or one of the four submenus.</summary>
public enum BattleMenuKind
{
    Root,
    Technique,
    Digivolve,
    Item,
    Switch,
    /// <summary>First page when switching in: choose the incoming partner's Digimon.</summary>
    SwitchIn,
    /// <summary>Second page when switching in: choose to swap partners or use the pair technique.</summary>
    PairSwitch,
}

/// <summary>One line of the battle menu: its label and whether it can be confirmed.</summary>
public sealed record BattleMenuLine(
    string Label,
    bool Enabled,
    BattleTextLine? Text = null,
    int Icon = -1,
    int Cost = -1,
    int Quantity = -1,
    string Description = "",
    BattleTextLine? DescriptionText = null,
    BattleTextLine? CostLabelText = null);

/// <summary>Source MFIGHT labels for the Technique and Item status windows.</summary>
public sealed record BattleMenuStatusText(BattleTextLine Header, BattleTextLine? CurrentOverride = null,
    BattleTextLine? Separator = null);

/// <summary>The engine-free view of the battle menu, with its source page slice.</summary>
public sealed record BattleMenuView(BattleMenuKind Menu, int Cursor, int First, int PageSize, int PageCount, IReadOnlyList<BattleMenuLine> Lines,
    BattleTextLine? PreviousPageText, BattleTextLine? NextPageText, BattleMenuStatusText? StatusText);

/// <summary>
/// The battle menu state machine. Technique and Item use the source's discrete L1/R1 pages,
/// not a fabricated vertical scrolling list. A page turn resets the cursor to row zero.
/// </summary>
public sealed class BattleMenuController
{
    public BattleMenuKind Menu { get; private set; } = BattleMenuKind.Root;
    /// <summary>Cursor row inside the current visible page.</summary>
    public int Cursor { get; private set; }
    /// <summary>Absolute source-list index at the current page's first row.</summary>
    public int First { get; private set; }
    public int PageSize { get; private set; }
    public int PageCount => Lines.Count == 0 ? 0 : (Lines.Count + PageSize - 1) / PageSize;
    public int SelectedIndex => First + Cursor;
    public IReadOnlyList<BattleMenuLine> Lines { get; private set; } = [];

    /// <summary>The visible source page and relative cursor for presentation.</summary>
    public BattleMenuView View => new(Menu, Cursor, First, PageSize, PageCount,
        Lines.Skip(First).Take(PageSize).ToArray(), PreviousPageText, NextPageText, StatusText);
    private BattleTextLine? PreviousPageText { get; set; }
    private BattleTextLine? NextPageText { get; set; }
    private BattleMenuStatusText? StatusText { get; set; }

    /// <summary>Opens a menu (or re-opens the root), on its first source page.</summary>
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
            BattleMenuKind.Technique => 6, // TECH_MENU_LINES
            BattleMenuKind.Item => 7,      // ITEM_MENU_LINES
            _ => Math.Max(1, lines.Count),
        };
        First = 0;
        Cursor = lines.Count == 0 ? 0 : Math.Clamp(cursor, 0, Math.Min(lines.Count, PageSize) - 1);
    }

    /// <summary>Opens (or returns to) the six-command root menu.</summary>
    public void OpenRoot(IReadOnlyList<BattleMenuLine> lines, int cursor = 0) => Open(BattleMenuKind.Root, lines, cursor);

    /// <summary>Moves the cursor up, clamped to the current source page.</summary>
    public bool MoveUp()
    {
        if (Cursor <= 0) return false;
        Cursor--;
        return true;
    }

    /// <summary>Moves the cursor down, clamped to the current page's populated rows.</summary>
    public bool MoveDown()
    {
        if (Cursor >= VisibleCount - 1) return false;
        Cursor++;
        return true;
    }

    /// <summary>Source PAD_L1 behavior.</summary>
    public bool PreviousPage() => SetPage(CurrentPage - 1);

    /// <summary>Source PAD_R1 behavior.</summary>
    public bool NextPage() => SetPage(CurrentPage + 1);

    /// <summary>Whether the selected source-list line can be confirmed (not greyed out).</summary>
    public bool CanConfirm => Lines.Count > 0 && SelectedIndex < Lines.Count && Lines[SelectedIndex].Enabled;

    private int CurrentPage => First / PageSize;
    private int VisibleCount => Math.Min(PageSize, Math.Max(0, Lines.Count - First));

    private bool SetPage(int page)
    {
        if (Menu is not (BattleMenuKind.Technique or BattleMenuKind.Item) || PageCount == 0) return false;
        var clamped = Math.Clamp(page, 0, PageCount - 1);
        if (clamped == CurrentPage) return false;
        First = clamped * PageSize;
        Cursor = 0;
        return true;
    }
}
