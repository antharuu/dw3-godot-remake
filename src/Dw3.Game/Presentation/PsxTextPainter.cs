using Dw3Production.Conversion;
using Godot;

namespace Dw3Production.Presentation;

/// <summary>Shared PSX text-window painter. It uses extracted glyph metrics and exact decoded
/// palette rows, and writes through the owning Control so HUD and STFGTREP share one path.</summary>
public sealed class PsxTextPainter
{
    private readonly Control _owner;
    private FontData? _font;
    private readonly Dictionary<(int Style, int Palette, int Page), Texture2D> _pages = [];

    public PsxTextPainter(Control owner) => _owner = owner;

    public void SetFont(FontData font)
    {
        _font = font;
        _pages.Clear();
        for (var style = 1; style <= 3; style++)
        {
            LoadFontStyle(font, style);
        }
    }

    public void DrawWindow(int style, int x, int y, string text, bool rightAlign, int palette) =>
        DrawString(new Vector2(x, y), text, rightAlign ? HorizontalAlignment.Right : HorizontalAlignment.Left, 0, palette, style);

    public void DrawLine(int style, int x, int y, BattleTextLine? line, bool rightAlign, int palette, int tokenLimit = int.MaxValue)
    {
        if (line is null || line == BattleTextLine.Missing)
        {
            return;
        }

        if (rightAlign)
        {
            throw new InvalidOperationException("right-aligned token window requires measured token stream");
        }

        var cursor = Vector2.Zero;
        DrawTokens(line.Tokens, line.Substitutions, new Vector2(x, y), ref cursor, palette, style, tokenLimit);
    }

    public void DrawString(Vector2 origin, string text, HorizontalAlignment alignment, float width, int palette, int style = 1)
    {
        if (_font is null || palette is < 0 or > 7)
        {
            return;
        }

        var selected = _font.Style(style);
        var glyphs = BuildGlyphList(selected, text);
        var x = ComputeAlignedX(origin.X, width, alignment, glyphs);
        foreach (var glyph in glyphs)
        {
            if (_pages.TryGetValue((style, palette, glyph.Page), out var page))
            {
                _owner.DrawTextureRectRegion(page, new Rect2(x + glyph.Dx, origin.Y + glyph.Dy, glyph.W, glyph.H), new Rect2(glyph.U, glyph.V, glyph.W, glyph.H), Colors.White);
            }

            x += glyph.Advance + glyph.Dx;
        }
    }

    private static List<GlyphMetric> BuildGlyphList(FontStyleData selected, string text)
    {
        var glyphs = new List<GlyphMetric>();
        foreach (var character in text)
        {
            var code = ToSourceGlyphCode(character);
            if (code >= 0 && selected.Glyphs.TryGetValue(code, out var glyph))
            {
                glyphs.Add(glyph);
            }
        }

        return glyphs;
    }

    private static float ComputeAlignedX(float originX, float width, HorizontalAlignment alignment, List<GlyphMetric> glyphs)
    {
        var advance = glyphs.Sum(glyph => glyph.Advance + glyph.Dx);
        var x = alignment == HorizontalAlignment.Right ? originX + width - advance : originX;
        if (alignment == HorizontalAlignment.Center)
        {
            x += (width - advance) / 2f;
        }

        return x;
    }

    private static int ToSourceGlyphCode(char character) => character switch
    {
        ' ' => 1,
        >= '0' and <= '9' => 4 + character - '0',
        >= 'A' and <= 'Z' => 14 + character - 'A',
        >= 'a' and <= 'z' => 40 + character - 'a',
        _ => -1,
    };

    private void LoadFontStyle(FontData font, int style)
    {
        for (var palette = 0; palette < 8; palette++)
        {
            LoadFontStylePalette(font, style, palette);
        }
    }

    private void LoadFontStylePalette(FontData font, int style, int palette)
    {
        for (var page = 0; page < 2; page++)
        {
            var image = Image.LoadFromFile(font.PalettePagePath(style, palette, page));
            if (image is null || image.IsEmpty())
            {
                throw new CacheResourceException("CACHE_RESOURCE_UNAVAILABLE", $"font palette image unreadable style={style} palette={palette} page={page}");
            }

            _pages[(style, palette, page)] = ImageTexture.CreateFromImage(image);
        }
    }

    private void DrawTokens(IReadOnlyList<TextToken> tokens, IReadOnlyList<IReadOnlyList<TextToken>> substitutions,
        Vector2 origin, ref Vector2 cursor, int palette, int style, int tokenLimit)
    {
        if (_font is null || palette is < 0 or > 7)
        {
            return;
        }

        var drawn = 0;
        foreach (var token in tokens)
        {
            if (drawn >= tokenLimit)
            {
                return;
            }

            drawn += DrawToken(token, substitutions, origin, ref cursor, palette, style, tokenLimit - drawn);
        }
    }

    // S107: removed the redundant `selected` parameter; DrawToken now resolves the style itself.
    private int DrawToken(TextToken token, IReadOnlyList<IReadOnlyList<TextToken>> substitutions,
        Vector2 origin, ref Vector2 cursor, int palette, int style, int tokenLimit)
    {
        if (token.IsControl)
        {
            ApplyControl(token, substitutions, origin, ref cursor, palette, style, tokenLimit);
            return 0;
        }

        if (_font is not { } selectedFont)
        {
            return 0;
        }

        var selected = selectedFont.Style(style);
        var table = token.IsGlyph ? selected.Glyphs : selected.Icons;
        if (!table.TryGetValue(token.Code, out var glyph))
        {
            return 0;
        }

        if (_pages.TryGetValue((style, palette, glyph.Page), out var page))
        {
            _owner.DrawTextureRectRegion(page, new Rect2(origin + cursor + new Vector2(glyph.Dx, glyph.Dy), new Vector2(glyph.W, glyph.H)), new Rect2(glyph.U, glyph.V, glyph.W, glyph.H), Colors.White);
        }

        cursor.X += glyph.Advance + glyph.Dx;
        return 1;
    }

    private void ApplyControl(TextToken token, IReadOnlyList<IReadOnlyList<TextToken>> substitutions,
        Vector2 origin, ref Vector2 cursor, int palette, int style, int tokenLimit)
    {
        if (token.Command == 1)
        {
            cursor.X = 0;
            cursor.Y += _font!.Style(style).LineHeight;
        }
        else if (token.Command == 5 && token.Args.Count > 0)
        {
            var index = token.Args[0];
            if (index > 0 && index <= substitutions.Count)
            {
                DrawTokens(substitutions[index - 1], [], origin, ref cursor, palette, style, tokenLimit);
            }
        }
    }
}
