using Dw3.Formats.Battle;

namespace Dw3.Extractor;

/// <summary>
/// Resolves the palette the GPU reads for a battle sprite part, following the source CLUT packing
/// instead of assuming every CLUT lies in the sheet's own texture pages.
/// </summary>
/// <remarks>
/// The source draw routine adds the sheet placement, the part CLUT row, the frame header row and the
/// animated row, then packs the result as <c>(y &lt;&lt; 6) | ((x &gt;&gt; 4) &amp; 0x3F)</c> in sixteen bits. The GPU keeps
/// nine row bits, so a row past the bottom of VRAM wraps to the top. Only one external region is
/// accepted: the 64-column, 256-row rectangle at VRAM (448, 0). The boot and country-select full
/// clears zero it, the movie player clears all VRAM when it stops, and no loader, transfer, fill or
/// drawing area of any game mode writes it afterwards (the font stops at column 447, the battle
/// images start at 512 and every drawing area stays below column 320). A palette read there is
/// sixteen zero words. Any other out-of-page address stays unsupported rather than guessed.
/// </remarks>
internal sealed class SpriteEffectPaletteResolver
{
    /// <summary>Halfwords per palette.</summary>
    private const int PaletteSize = 16;

    /// <summary>Halfword columns of one loaded texture page.</summary>
    private const int PageWords = HudTexturePage.PageWidthWords;

    /// <summary>First column of the never-written VRAM rectangle.</summary>
    private const int ClearedLeft = 448;

    /// <summary>Column just past the never-written VRAM rectangle.</summary>
    private const int ClearedRight = 512;

    /// <summary>Row just past the never-written VRAM rectangle, which starts at row 0.</summary>
    private const int ClearedBottom = 256;

    private readonly IReadOnlyList<HudTexturePage> _pages;
    private readonly IReadOnlyList<int>? _placement;

    /// <summary>Initializes a new instance of the <see cref="SpriteEffectPaletteResolver"/> class.</summary>
    /// <param name="pages">The sheet's loaded texture pages, side by side from its placement.</param>
    /// <param name="placement">The sheet VRAM placement (x, y), or null when it is unknown, which disables external resolution.</param>
    internal SpriteEffectPaletteResolver(IReadOnlyList<HudTexturePage> pages, IReadOnlyList<int>? placement)
    {
        if (placement is not null && placement.Count != 2)
        {
            throw new FormatException("Sprite sheet placement must hold an x and a y.");
        }

        _pages = pages;
        _placement = placement;
    }

    /// <summary>Resolves the palette at a sheet-relative CLUT position.</summary>
    /// <param name="clutX">Part CLUT column relative to the sheet placement.</param>
    /// <param name="clutY">Part CLUT row plus frame header row plus animated row, relative to the placement.</param>
    /// <returns>The palette words and their source.</returns>
    /// <exception cref="FormatException">The CLUT is outside the loaded pages and outside the never-written VRAM rectangle.</exception>
    internal SpriteEffectPalette Resolve(int clutX, int clutY)
    {
        (int vramX, int vramY) = Pack(clutX, clutY);
        if (clutY >= 0 && clutY < HudTexturePage.PageHeight && clutX >= 0 && clutX / PageWords < _pages.Count)
        {
            return new SpriteEffectPalette(HudFrameRenderer.ReadPalette(_pages, clutX, clutY, false), "effect-page", vramX, vramY);
        }

        if (_placement is not null && vramX >= ClearedLeft && vramX + PaletteSize <= ClearedRight && vramY < ClearedBottom)
        {
            return new SpriteEffectPalette(new ushort[PaletteSize], "cleared-vram", vramX, vramY);
        }

        throw new FormatException("Sprite palette outside loaded texture.");
    }

    /// <summary>Applies the source CLUT packing to an absolute VRAM address.</summary>
    /// <param name="clutX">Sheet-relative column.</param>
    /// <param name="clutY">Sheet-relative row.</param>
    /// <returns>The column and row the GPU reads, or (-1, -1) without a placement.</returns>
    private (int X, int Y) Pack(int clutX, int clutY)
    {
        if (_placement is null)
        {
            return (-1, -1);
        }

        int x = _placement[0] + clutX;
        int y = _placement[1] + clutY;
        return (((x >> 4) & 0x3F) << 4, y & 0x1FF);
    }
}
