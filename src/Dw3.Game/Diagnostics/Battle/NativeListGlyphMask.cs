using Dw3Production.Conversion;
using Dw3Production.Presentation;
using Godot;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>Renders an independent glyph mask for comparison with submitted HUD pixels.</summary>
internal sealed partial class NativeListGlyphMask : Control
{
    private readonly PsxTextPainter _painter;
    private readonly BattleHudSprites _sprites = new();
    private int _icon = -1;
    private BattleTextLine? _line;
    private Vector2I _anchor;
    private int _palette;
    private string? _literal;
    private int _style;
    private bool _rightAlign;

    /// <summary>Initializes a new instance of the <see cref="NativeListGlyphMask"/> class.</summary>
    public NativeListGlyphMask()
    {
        _painter = new PsxTextPainter(this);
    }

    /// <summary>Binds font, decoded tokens and palette for a single independent text submission.</summary>
    /// <param name="font">Prepared font.</param>
    /// <param name="line">Expected catalogue text.</param>
    /// <param name="anchor">Source coordinates.</param>
    /// <param name="palette">Source CLUT row.</param>
    public void Bind(FontData font, BattleTextLine line, Vector2I anchor, int palette)
    {
        _painter.SetFont(font);
        _line = line;
        _icon = -1;
        _literal = null;
        _style = 1;
        _anchor = anchor;
        _palette = palette;
        QueueRedraw();
    }

    /// <summary>Binds a literal numeric window using the prepared font.</summary>
    /// <param name="font">Prepared font.</param>
    /// <param name="text">Expected literal.</param>
    /// <param name="anchor">Source coordinates.</param>
    /// <param name="style">Source style.</param>
    /// <param name="rightAlign">Whether the window ends at its anchor.</param>
    public void BindWindow(FontData font, string text, Vector2I anchor, int style, bool rightAlign)
    {
        _painter.SetFont(font);
        _line = null;
        _icon = -1;
        _literal = text;
        _anchor = anchor;
        _style = style;
        _rightAlign = rightAlign;
        QueueRedraw();
    }

    /// <summary>Binds an extracted skill marker.</summary>
    /// <param name="icons">Prepared marker frames.</param>
    /// <param name="icon">Expected source icon identifier.</param>
    /// <param name="anchor">Source coordinates.</param>
    public void BindIcon(BattleMenuIconsData icons, int icon, Vector2I anchor)
    {
        _sprites.LoadMenuIcons(icons);
        _line = null;
        _literal = null;
        _icon = icon;
        _anchor = anchor;
        QueueRedraw();
    }

    /// <inheritdoc/>
    public override void _Draw()
    {
        if (_line is not null)
        {
            _painter.DrawLine(1, _anchor.X, _anchor.Y, _line, false, _palette);
        }

        if (_literal is not null)
        {
            _painter.DrawWindow(_style, _anchor.X, _anchor.Y, _literal, _rightAlign, 0);
        }

        if (_icon >= 0)
        {
            _sprites.DrawMenuIcon(this, _icon, _anchor.X, _anchor.Y, 0);
        }
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _sprites.Dispose();
        }

        base.Dispose(disposing);
    }
}
