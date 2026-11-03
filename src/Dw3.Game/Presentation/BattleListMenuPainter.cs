using System.Globalization;
using Dw3Production.Simulation;

namespace Dw3Production.Presentation;

/// <summary>
/// Draws the paged technique and item lists: an icon and a name per line from (0x1D, 0x45) at
/// 0xE per line, blinking page arrows, the page labels, the selected line's description and its
/// cost or quantity. The list cursor has no line sprite.
/// </summary>
internal static class BattleListMenuPainter
{
    private const int TechniqueLines = 6;
    private const int ItemLines = 7;

    /// <summary>Draws the technique list with the active fighter's MP.</summary>
    /// <param name="canvas">The HUD drawing context.</param>
    /// <param name="view">The battle view of this frame.</param>
    /// <param name="menu">The technique menu.</param>
    public static void DrawTechnique(BattleHudCanvas canvas, BattleView view, BattleMenuView menu)
    {
        canvas.Frame(0x2A, 8, 0x3E);
        canvas.Frame(0x29, 0xA3, 0x21);
        canvas.Frame(0x31, 0xB, 0xBC);
        DrawPageArrows(canvas, menu, 0x9F);
        DrawPageLabels(canvas, menu, 0x9F);
        DrawMpStatus(canvas, menu, view.Players.FirstOrDefault(fighter => fighter.Active));
        DrawLines(canvas, menu, TechniqueLines);
        var selected = Selected(menu);
        var palette = selected?.Enabled == false ? BattleHudPalette.Grey : BattleHudPalette.White;
        canvas.Text.DrawLine(1, 0x14, 0xC2, selected?.DescriptionText, selected?.Description ?? string.Empty, rightAlign: false, palette: palette);
        canvas.Text.DrawLine(1, 0x100, 0xD0, selected?.CostLabelText, string.Empty, rightAlign: false, palette: palette);
        if (selected is not null && selected.Cost >= 0)
        {
            canvas.Text.DrawWindow(1, 0x12B, 0xD0, selected.Cost.ToString(CultureInfo.InvariantCulture), rightAlign: true, palette: selected.Enabled ? BattleHudPalette.White : BattleHudPalette.Grey);
        }

        canvas.Cursor(14, 69 + (menu.Cursor * 14));
    }

    /// <summary>Draws the item list with the selected item's quantity.</summary>
    /// <param name="canvas">The HUD drawing context.</param>
    /// <param name="menu">The item menu.</param>
    public static void DrawItem(BattleHudCanvas canvas, BattleMenuView menu)
    {
        canvas.Frame(0x27, 8, 0x3E);
        canvas.Frame(0x28, 0xA6, 0xA0);
        canvas.Frame(0x31, 0xB, 0xBC);
        DrawPageArrows(canvas, menu, 0xA9);
        DrawPageLabels(canvas, menu, 0xA9);
        DrawLines(canvas, menu, ItemLines);
        var selected = Selected(menu);
        canvas.Text.DrawLine(1, 0x14, 0xC2, selected?.DescriptionText, selected?.Description ?? string.Empty, rightAlign: false, palette: BattleHudPalette.White);
        canvas.Text.DrawLine(1, 0xAC, 0xA5, menu.StatusText?.Header, string.Empty, rightAlign: false, palette: BattleHudPalette.White);
        if (selected is not null && selected.Quantity >= 0)
        {
            canvas.Text.DrawWindow(1, 0xC6, 0xA5, selected.Quantity.ToString(CultureInfo.InvariantCulture), rightAlign: true, palette: BattleHudPalette.White);
        }

        canvas.Cursor(14, 69 + (menu.Cursor * 14));
    }

    /// <summary>Gets the selected menu line when the menu contains at least one line.</summary>
    /// <param name="menu">Menu whose selection is queried.</param>
    /// <returns>The selected line, or null for an empty menu.</returns>
    private static BattleMenuLine? Selected(BattleMenuView menu) => menu.Lines.Count == 0 ? null : menu.Lines[menu.Cursor];

    /// <summary>The MP header, current MP (or its override text), separator and maximum MP at y 0x3A.</summary>
    /// <param name="canvas">The HUD drawing context.</param>
    /// <param name="menu">The technique menu, whose status text may override the current MP.</param>
    /// <param name="player">The active player fighter.</param>
    private static void DrawMpStatus(BattleHudCanvas canvas, BattleMenuView menu, BattleFighterView player)
    {
        canvas.Text.DrawLine(3, 0xAC, 0x3A, menu.StatusText?.Header, string.Empty, rightAlign: false, palette: BattleHudPalette.White);
        if (menu.StatusText?.CurrentOverride is { } currentOverride)
        {
            canvas.Text.DrawLine(3, 0xD8, 0x3A, currentOverride, string.Empty, rightAlign: true, palette: BattleHudPalette.White);
        }
        else
        {
            canvas.Text.DrawWindow(3, 0xD8, 0x3A, player.Mp.ToString(CultureInfo.InvariantCulture), rightAlign: true, palette: BattleHudPalette.White);
        }

        canvas.Text.DrawLine(3, 0xD9, 0x3A, menu.StatusText?.Separator, string.Empty, rightAlign: false, palette: BattleHudPalette.White);
        canvas.Text.DrawWindow(3, 0xFB, 0x3A, player.MaxMp.ToString(CultureInfo.InvariantCulture), rightAlign: true, palette: BattleHudPalette.White);
    }

    /// <summary>Draws the visible icon and label rows of a paged menu.</summary>
    /// <param name="canvas">The HUD drawing context.</param>
    /// <param name="menu">Menu containing the rows.</param>
    /// <param name="maxLines">Maximum number of rows visible in the pane.</param>
    private static void DrawLines(BattleHudCanvas canvas, BattleMenuView menu, int maxLines)
    {
        for (var i = 0; i < menu.Lines.Count && i < maxLines; i++)
        {
            var line = menu.Lines[i];
            canvas.MenuIcon(line.Icon, 0x1D, 0x45 + (i * 0xE));
            canvas.Text.DrawLine(1, 0x2A, 0x45 + (i * 0xE), line.Text, line.Label, rightAlign: false, line.Enabled ? BattleHudPalette.White : BattleHudPalette.Grey);
        }
    }

    /// <summary>Blinks frames 0x1F (previous page) and 0x20 (next page) when that page exists.</summary>
    /// <param name="canvas">The HUD drawing context.</param>
    /// <param name="menu">The paged menu.</param>
    /// <param name="y">Source y of the arrow row: 0x9F for techniques, 0xA9 for items.</param>
    private static void DrawPageArrows(BattleHudCanvas canvas, BattleMenuView menu, int y)
    {
        if ((canvas.Clock.Milliseconds & 0x10) == 0)
        {
            return;
        }

        if (menu.First > 0)
        {
            canvas.Frame(0x1F, 0x10, y);
        }

        if ((menu.First / menu.PageSize) + 1 < menu.PageCount)
        {
            canvas.Frame(0x20, 0x90, y);
        }
    }

    /// <summary>Draws the labels beside a menu's page arrows.</summary>
    /// <param name="canvas">The HUD drawing context.</param>
    /// <param name="menu">Menu providing the page labels.</param>
    /// <param name="y">Source y coordinate of the label row.</param>
    private static void DrawPageLabels(BattleHudCanvas canvas, BattleMenuView menu, int y)
    {
        canvas.Text.DrawLine(3, 0x1A, y, menu.PreviousPageText, string.Empty, rightAlign: false, palette: BattleHudPalette.DarkBlue);
        canvas.Text.DrawLine(3, 0x80, y, menu.NextPageText, string.Empty, rightAlign: false, palette: BattleHudPalette.DarkBlue);
    }
}
