using Dw3Production.Conversion;
using Godot;

namespace Dw3Production.Presentation;

/// <summary>Shared PSX text-window painter. It uses extracted glyph metrics and exact decoded
/// palette rows, and writes through the owning Control so HUD and STFGTREP share one path.</summary>
public sealed class PsxTextPainter
{
    private readonly Control _owner;

    private readonly Dictionary<(int Style, int Palette, int Page), Texture2D> _pages = [];

    private FontData? _font;

    /// <summary>
    /// Initializes a new instance of the <see cref="PsxTextPainter"/> class.
    /// </summary>
    /// <param name="owner">The Godot Control node whose DrawTextureRectRegion is called for all glyph and icon draws.</param>
    public PsxTextPainter(Control owner) => _owner = owner;

    /// <summary>Loads font metrics and palette pages used by text drawing.</summary>
    /// <param name="font">Decoded font data and palette-page paths.</param>
    public void SetFont(FontData font)
    {
        _font = font;
        _pages.Clear();
        for (var style = 1; style <= 3; style++)
        {
            LoadFontStyle(font, style);
        }
    }

    /// <summary>Draws a plain string in a positioned text window.</summary>
    /// <param name="style">Font style identifier.</param>
    /// <param name="x">Window horizontal coordinate.</param>
    /// <param name="y">Window vertical coordinate.</param>
    /// <param name="text">String to draw.</param>
    /// <param name="rightAlign">Indicates whether the string is right aligned.</param>
    /// <param name="palette">Palette row used by glyphs.</param>
    public void DrawWindow(int style, int x, int y, string text, bool rightAlign, int palette) =>
        DrawString(new Vector2(x, y), text, rightAlign ? HorizontalAlignment.Right : HorizontalAlignment.Left, 0, palette, style);

    /// <summary>Draws a tokenized text line with optional reveal limiting.</summary>
    /// <param name="style">Font style identifier.</param>
    /// <param name="x">Line horizontal coordinate.</param>
    /// <param name="y">Line vertical coordinate.</param>
    /// <param name="line">Tokenized line to draw.</param>
    /// <param name="rightAlign">Indicates whether the line is right aligned.</param>
    /// <param name="palette">Palette row used by glyphs.</param>
    /// <param name="tokenLimit">Maximum number of tokens drawn.</param>
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

    /// <summary>Draws a string using glyph metrics and the selected alignment.</summary>
    /// <param name="origin">Window origin.</param>
    /// <param name="text">String to draw.</param>
    /// <param name="alignment">Horizontal alignment mode.</param>
    /// <param name="width">Available alignment width.</param>
    /// <param name="palette">Palette row used by glyphs.</param>
    /// <param name="style">Font style identifier.</param>
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

    /// <summary>Converts supported characters into drawable glyph metrics.</summary>
    /// <param name="selected">Font style containing glyph metrics.</param>
    /// <param name="text">Text to convert.</param>
    /// <returns>Glyph metrics for supported characters.</returns>
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

    /// <summary>Calculates the first glyph coordinate for an alignment mode.</summary>
    /// <param name="originX">Unaligned horizontal origin.</param>
    /// <param name="width">Available alignment width.</param>
    /// <param name="alignment">Alignment mode.</param>
    /// <param name="glyphs">Glyphs whose advance is measured.</param>
    /// <returns>Aligned horizontal coordinate.</returns>
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

    /// <summary>Maps a supported character to its glyph-table code.</summary>
    /// <param name="character">Character to map.</param>
    /// <returns>Glyph-table code or a negative value when unsupported.</returns>
    private static int ToSourceGlyphCode(char character) => character switch
    {
        ' ' => 1,
        >= '0' and <= '9' => 4 + character - '0',
        >= 'A' and <= 'Z' => 14 + character - 'A',
        >= 'a' and <= 'z' => 40 + character - 'a',
        _ => -1,
    };

    /// <summary>Loads every palette row for one font style.</summary>
    /// <param name="font">Decoded font data.</param>
    /// <param name="style">Style identifier to load.</param>
    private void LoadFontStyle(FontData font, int style)
    {
        for (var palette = 0; palette < 8; palette++)
        {
            LoadFontStylePalette(font, style, palette);
        }
    }

    /// <summary>Loads both atlas pages for one style and palette row.</summary>
    /// <param name="font">Decoded font data.</param>
    /// <param name="style">Style identifier to load.</param>
    /// <param name="palette">Palette row to load.</param>
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

    /// <summary>Draws tokens until the configured reveal limit is reached.</summary>
    /// <param name="tokens">Token sequence to draw.</param>
    /// <param name="substitutions">Replacement token sequences.</param>
    /// <param name="origin">Text origin.</param>
    /// <param name="cursor">Mutable text cursor.</param>
    /// <param name="palette">Palette row used by glyphs.</param>
    /// <param name="style">Font style identifier.</param>
    /// <param name="tokenLimit">Maximum number of tokens drawn.</param>
    private void DrawTokens(
        IReadOnlyList<TextToken> tokens,
        IReadOnlyList<IReadOnlyList<TextToken>> substitutions,
        Vector2 origin,
        ref Vector2 cursor,
        int palette,
        int style,
        int tokenLimit)
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

    /// <summary>Draws one token or applies its control action using its selected font style.</summary>
    /// <param name="token">Token to process.</param>
    /// <param name="substitutions">Replacement token sequences.</param>
    /// <param name="origin">Text origin.</param>
    /// <param name="cursor">Mutable text cursor.</param>
    /// <param name="palette">Palette row used by glyphs.</param>
    /// <param name="style">Font style identifier.</param>
    /// <param name="tokenLimit">Remaining reveal budget.</param>
    /// <returns>Number of glyph tokens drawn.</returns>
    private int DrawToken(
        TextToken token,
        IReadOnlyList<IReadOnlyList<TextToken>> substitutions,
        Vector2 origin,
        ref Vector2 cursor,
        int palette,
        int style,
        int tokenLimit)
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

    /// <summary>Applies a line-break or substitution control token.</summary>
    /// <param name="token">Control token to apply.</param>
    /// <param name="substitutions">Replacement token sequences.</param>
    /// <param name="origin">Text origin.</param>
    /// <param name="cursor">Mutable text cursor.</param>
    /// <param name="palette">Palette row used by glyphs.</param>
    /// <param name="style">Font style identifier.</param>
    /// <param name="tokenLimit">Remaining reveal budget.</param>
    private void ApplyControl(
        TextToken token,
        IReadOnlyList<IReadOnlyList<TextToken>> substitutions,
        Vector2 origin,
        ref Vector2 cursor,
        int palette,
        int style,
        int tokenLimit)
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
