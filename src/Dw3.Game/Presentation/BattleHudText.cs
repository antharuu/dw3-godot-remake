using Dw3Production.Conversion;
using Godot;

namespace Dw3Production.Presentation;

/// <summary>
/// PSX text-window drawing for the battle HUD. A text line that the battle text catalog did not
/// resolve is never replaced by invented text: it is skipped and its fallback key is warned once.
/// </summary>
internal sealed class BattleHudText
{
    private readonly PsxTextPainter _painter;
    private readonly HashSet<string> _unsourcedWarnings = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="BattleHudText"/> class.
    /// </summary>
    /// <param name="owner">The HUD control the text is drawn on.</param>
    public BattleHudText(Control owner)
    {
        _painter = new PsxTextPainter(owner);
    }

    /// <summary>Binds the decoded font atlas.</summary>
    /// <param name="font">Decoded font metrics and pages.</param>
    public void SetFont(FontData font) => _painter.SetFont(font);

    /// <summary>Draws a literal string in a text window.</summary>
    /// <param name="style">Font style of the window.</param>
    /// <param name="x">Source x of the window anchor.</param>
    /// <param name="y">Source y of the window anchor.</param>
    /// <param name="text">Text drawn with source glyph codes.</param>
    /// <param name="rightAlign">True to end the text at <paramref name="x"/>.</param>
    /// <param name="palette">CLUT row 0..7, see <see cref="BattleHudPalette"/>.</param>
    public void DrawWindow(int style, int x, int y, string text, bool rightAlign, int palette)
        => _painter.DrawWindow(style, x, y, text, rightAlign, palette);

    /// <summary>Draws a resolved text line, or warns once about its fallback key.</summary>
    /// <param name="style">Font style of the window.</param>
    /// <param name="x">Source x of the window anchor.</param>
    /// <param name="y">Source y of the window anchor.</param>
    /// <param name="line">Resolved line, or null / <see cref="BattleTextLine.Missing"/> when unsourced.</param>
    /// <param name="fallback">Key named in the warning when the line is unsourced.</param>
    /// <param name="rightAlign">True to end the text at <paramref name="x"/>.</param>
    /// <param name="palette">CLUT row 0..7, see <see cref="BattleHudPalette"/>.</param>
    public void DrawLine(int style, int x, int y, BattleTextLine? line, string fallback, bool rightAlign, int palette)
    {
        if (line is null || line == BattleTextLine.Missing)
        {
            WarnUnsourced(fallback);
            return;
        }

        _painter.DrawLine(style, x, y, line, rightAlign, palette);
    }

    private void WarnUnsourced(string key)
    {
        if (_unsourcedWarnings.Add(key))
        {
            GD.PushWarning($"WARN battle-text-unsourced key={key}");
        }
    }
}
