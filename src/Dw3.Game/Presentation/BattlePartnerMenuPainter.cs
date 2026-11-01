using System.Globalization;
using Dw3Production.Simulation;
using Godot;

namespace Dw3Production.Presentation;

/// <summary>
/// Draws the partner menus of the player's turn: the switch list of benched partners with their HP
/// and MP, the incoming-form pane of a switch-in or pair switch, and the digivolution form list.
/// </summary>
internal static class BattlePartnerMenuPainter
{
    /// <summary>Draws one line per benched, present partner: backing sprite 0x2F (its five CLUT
    /// rows cycle on the selected line), name, HP and MP.</summary>
    /// <param name="canvas">The HUD drawing context.</param>
    /// <param name="view">The battle view of this frame.</param>
    /// <param name="menu">The switch menu.</param>
    public static void DrawSwitch(BattleHudCanvas canvas, BattleView view, BattleMenuView menu)
    {
        var candidates = view.Players.Where(fighter => !fighter.Active && fighter.Id != 0).ToArray();
        var cursorFrame = canvas.CursorFrame;
        for (var i = 0; i < menu.Lines.Count && i < candidates.Length; i++)
        {
            DrawSwitchCandidate(canvas, menu, i, candidates[i]);
        }

        // The pair icon 0x30 stays absent until a real pair-technique relation is recovered.
        canvas.Sprites.DrawCursor(canvas.Canvas, new Vector2(17, 78 + (menu.Cursor * 32)), cursorFrame);
    }

    /// <summary>Draws the incoming-form pane (frame 0x2B) of a switch-in, or the two-choice pane of
    /// a pair switch.</summary>
    /// <param name="canvas">The HUD drawing context.</param>
    /// <param name="menu">The switch-in or pair-switch menu.</param>
    public static void DrawSwitchIn(BattleHudCanvas canvas, BattleMenuView menu)
    {
        canvas.Frame(0x2B, 0xA7, 0x8F);
        if (menu.Menu == BattleMenuKind.PairSwitch)
        {
            DrawPairSwitch(canvas, menu);
            return;
        }

        var formBlinkRow = canvas.BlinkRow;
        for (var i = 0; i < menu.Lines.Count; i++)
        {
            // Sprite 0x24 backs each incoming-form line.
            canvas.Frame(0x24, 167, 146 + (i * 19), i == menu.Cursor ? formBlinkRow : 0);
            canvas.Text.DrawLine(1, 0xBB, 0x92 + (i * 0x13), menu.Lines[i].Text, menu.Lines[i].Label, rightAlign: false, palette: BattleHudPalette.White);
        }

        canvas.Cursor(168, 147 + (menu.Cursor * 19));
    }

    /// <summary>Draws the digivolution forms on the right-side partner pane (frame 0x2B).</summary>
    /// <param name="canvas">The HUD drawing context.</param>
    /// <param name="menu">The digivolve menu.</param>
    public static void DrawDigivolve(BattleHudCanvas canvas, BattleMenuView menu)
    {
        canvas.Frame(0x2B, 0xA7, 0x8F);
        for (var i = 0; i < menu.Lines.Count; i++)
        {
            var line = menu.Lines[i];
            canvas.Frame(0x24, 167, 146 + (i * 19));
            canvas.Text.DrawLine(1, 0xBB, 0x92 + (i * 0x13), line.Text, line.Label, rightAlign: false, line.Enabled ? BattleHudPalette.White : BattleHudPalette.Grey);
        }

        canvas.Cursor(168, 147 + (menu.Cursor * 19));
    }

    private static void DrawSwitchCandidate(BattleHudCanvas canvas, BattleMenuView menu, int i, BattleFighterView fighter)
    {
        var y = 0x4E + (i * 0x20);
        var palette = fighter.KnockedOut ? BattleHudPalette.Grey : BattleHudPalette.White;
        var blinkRow = i == menu.Cursor ? canvas.BlinkRow : 0;
        canvas.Frame(0x2F, 16, 77 + (i * 32), blinkRow);
        canvas.Text.DrawLine(1, 0x24, 0x4C + (i * 0x20), menu.Lines[i].Text, menu.Lines[i].Label, rightAlign: false, palette);
        DrawStat(canvas, y, "HP", fighter.Hp, fighter.MaxHp);
        DrawStat(canvas, y + 0xE, "MP", fighter.Mp, fighter.MaxMp);
    }

    private static void DrawStat(BattleHudCanvas canvas, int y, string label, int current, int maximum)
    {
        canvas.Text.DrawWindow(3, 0x6C, y, label, rightAlign: false, palette: BattleHudPalette.White);
        canvas.Text.DrawWindow(3, 0x98, y, current.ToString(CultureInfo.InvariantCulture), rightAlign: true, palette: BattleHudPalette.White);
        canvas.Text.DrawWindow(3, 0xBB, y, maximum.ToString(CultureInfo.InvariantCulture), rightAlign: true, palette: BattleHudPalette.White);
    }

    /// <summary>
    /// The pair switch shows the incoming form twice and two choice lines (sprite 0x22). The source
    /// draws frame 0x29 only when a pair technique exists, which the reconstructed state has not,
    /// so that panel is not invented.
    /// </summary>
    /// <param name="canvas">The HUD drawing context.</param>
    /// <param name="menu">The pair-switch menu; its first line names the incoming form.</param>
    private static void DrawPairSwitch(BattleHudCanvas canvas, BattleMenuView menu)
    {
        canvas.Text.DrawLine(1, 0xAB, 0x92, menu.Lines[0].Text, menu.Lines[0].Label, rightAlign: false, palette: BattleHudPalette.White);
        canvas.Text.DrawLine(1, 0xBB, 0xA5, menu.Lines[0].Text, menu.Lines[0].Label, rightAlign: false, palette: BattleHudPalette.White);
        var choiceBlinkRow = canvas.BlinkRow;
        for (var i = 0; i < menu.Lines.Count && i < 2; i++)
        {
            canvas.Frame(0x22, 167, 165 + (i * 19), i == menu.Cursor ? choiceBlinkRow : 0);
        }

        canvas.Cursor(168, 166 + (menu.Cursor * 19));
    }
}
