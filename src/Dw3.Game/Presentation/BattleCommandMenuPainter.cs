using Dw3Production.Simulation;
using Godot;

namespace Dw3Production.Presentation;

/// <summary>
/// Draws the command menu of the player's turn: the six root commands in the left column (x 0x24,
/// y 0x6D plus 0x13 per line) or the open submenu. The root cursor erases its previous position
/// with the cursor "off" image, so the painter remembers the last drawn root cursor.
/// </summary>
internal sealed class BattleCommandMenuPainter
{
    private const int MenuX = 0x24;
    private const int MenuY0 = 0x6D;
    private const int MenuLineStep = 0x13;

    private int _previousCursor = -1;

    /// <summary>Draws the menu of the view, if any.</summary>
    /// <param name="canvas">The HUD drawing context.</param>
    /// <param name="view">The battle view of this frame.</param>
    /// <exception cref="InvalidOperationException">The menu kind is unknown.</exception>
    public void Draw(BattleHudCanvas canvas, BattleView view)
    {
        if (view.Menu is not { } menu)
        {
            return;
        }

        switch (menu.Menu)
        {
            case BattleMenuKind.Root:
                DrawRoot(canvas, menu);
                break;
            case BattleMenuKind.Switch:
                BattlePartnerMenuPainter.DrawSwitch(canvas, view, menu);
                break;
            case BattleMenuKind.SwitchIn:
            case BattleMenuKind.PairSwitch:
                BattlePartnerMenuPainter.DrawSwitchIn(canvas, menu);
                break;
            case BattleMenuKind.Technique:
                BattleListMenuPainter.DrawTechnique(canvas, view, menu);
                break;
            case BattleMenuKind.Digivolve:
                BattlePartnerMenuPainter.DrawDigivolve(canvas, menu);
                break;
            case BattleMenuKind.Item:
                BattleListMenuPainter.DrawItem(canvas, menu);
                break;
            default:
                throw new InvalidOperationException($"unknown battle menu {menu.Menu}");
        }
    }

    private void DrawRoot(BattleHudCanvas canvas, BattleMenuView menu)
    {
        var cursorFrame = canvas.CursorFrame;
        for (var i = 0; i < menu.Lines.Count; i++)
        {
            var blinkRow = i == menu.Cursor ? canvas.BlinkRow : 0;
            canvas.Frame(0x22, 16, 109 + (i * 19), blinkRow);
            var palette = menu.Lines[i].Enabled ? BattleHudPalette.White : BattleHudPalette.Grey;
            canvas.Text.DrawLine(1, MenuX, MenuY0 + (i * MenuLineStep), menu.Lines[i].Text, string.Empty, rightAlign: false, palette);
        }

        if (_previousCursor >= 0 && _previousCursor != menu.Cursor)
        {
            canvas.Sprites.DrawCursorOff(canvas.Canvas, new Vector2(17, 110 + (_previousCursor * 19)));
        }

        canvas.Sprites.DrawCursor(canvas.Canvas, new Vector2(17, 110 + (menu.Cursor * 19)), cursorFrame);
        _previousCursor = menu.Cursor;
    }
}
