namespace Dw3Production.Conversion;

// Engine-free runtime contracts of the dialogue, text and font domains, read by
// DialogueDataPackLoader. The battle text reuses the same token and font records.

/// <summary>One talk condition (code, value) resolved to its condition family.</summary>
public sealed record DialogueCondition(int Code, int Value, string Kind);
/// <summary>One talk action (code, value) resolved to its action family. Actions are carried
/// verbatim and signalled; the dialogue runtime never applies them.</summary>
public sealed record DialogueAction(int Code, int Value, string Kind);
/// <summary>One actor talk: its text entry, conditions, actions, the derived effect kind
/// (simple | effect) and whether the snapshot progress selected it.</summary>
public sealed record DialogueTalk(int Entry, IReadOnlyList<DialogueCondition> Conditions, IReadOnlyList<DialogueAction> Actions, string Kind, IReadOnlyList<string> Families, bool Selected);
/// <summary>A dialogue actor at its tile position, direction and half-width box.</summary>
public sealed record DialogueActor(int Id, int X, int Y, int Dir, int? Width, IReadOnlyList<DialogueTalk> Talks);
/// <summary>A zone's dialogue: the index of its FSTALK text file and its actors.</summary>
public sealed record ZoneDialogue(int TalkFile, IReadOnlyList<DialogueActor> Actors);
/// <summary>One decoded text token: a glyph (t=g, code 4..233), an icon (t=i, index 1..114)
/// or a control (t=c, command 0..9 with its arguments).</summary>
public sealed record TextToken(string Type, int Code, int Command, IReadOnlyList<int> Args)
{
    public bool IsGlyph => Type == "g";
    public bool IsIcon => Type == "i";
    public bool IsControl => Type == "c";
}
/// <summary>One glyph/icon metric row: the atlas source rectangle plus draw offset and advance.</summary>
public sealed record GlyphMetric(int Page, int U, int V, int ClutX, int ClutY, int W, int H, int Dx, int Dy, int Advance);
/// <summary>One of the executable's three font styles. It is data, not a fallback policy:
/// callers must explicitly choose style 1, 2 or 3.</summary>
public sealed record FontStyleData(IReadOnlyDictionary<int, GlyphMetric> Glyphs, IReadOnlyDictionary<int, GlyphMetric> Icons, int LineHeight);
/// <summary>The dialogue font: per-code glyph and icon metrics of style 1, its two atlas page
/// paths, its line height (14), every style and the directory of the palette pages.</summary>
public sealed record FontData(IReadOnlyDictionary<int, GlyphMetric> Glyphs, IReadOnlyDictionary<int, GlyphMetric> Icons, string Page0Path, string Page1Path, int LineHeight, IReadOnlyDictionary<int, FontStyleData> Styles, string FontDirectory)
{
    public FontStyleData Style(int style) => Styles.TryGetValue(style, out var data)
        ? data : throw new CacheResourceException("CACHE_INVALID", $"font style missing: {style}");
    public string PalettePagePath(int style, int palette, int page)
    {
        if (style is < 1 or > 3 || palette is < 0 or > 7 || page is < 0 or > 1)
            throw new CacheResourceException("CACHE_INVALID", $"font palette path invalid: style={style} palette={palette} page={page}");
        var path = Path.Combine(FontDirectory, $"font-style-{style}-palette-{palette}-page-{page}.png");
        return File.Exists(path) ? path : throw new CacheResourceException("CACHE_RESOURCE_UNAVAILABLE", $"font palette page missing: {path}");
    }
}
/// <summary>The full dialogue runtime contract: zones -> actors -> talks, indexed text streams
/// and the font.</summary>
public sealed record DialogueData(IReadOnlyDictionary<string, ZoneDialogue> Zones, IReadOnlyDictionary<int, IReadOnlyList<IReadOnlyList<TextToken>>> Texts, FontData Font);
/// <summary>One talk-box frame sprite composited to a standalone RGBA PNG. Frames: 0 top edge,
/// 2 bottom edge, 3/4/5/6 the four corner sprites, 7 the "next" arrow (<see cref="ClutFrames"/>
/// blink variants). <see cref="Semi"/> is true when the sheet frame is drawn semi-transparent.</summary>
public sealed record DialogueFrame(int Id, string Path, int W, int H, int ClutFrames, bool Semi);
/// <summary>Engine-free 2D integer coordinate. The Conversion records stay free of Godot types
/// so the standalone xUnit projects can compile them without a GodotSharp reference. The Godot
/// interop (implicit conversion to <c>Vector2</c>) lives in the Godot-only partial in Vec2I.Godot.cs.</summary>
public readonly partial record struct Vec2I(int X, int Y);
/// <summary>One of the executable's four talk-box layout records: the offsets of every box part,
/// in PSX screen pixels, relative to the box origin.</summary>
public sealed record TalkBoxLayout(int Sprite, Vec2I SpritePos, Vec2I Sprite0Pos, Vec2I Panel, Vec2I Sprite2Pos, int NameX, int NameY, int TextX, int TextY, Vec2I Arrow);
/// <summary>The talk-box panel: a 12x62 texture stretched over the box, semi-transparent at its
/// STP border texels. R/G/B is the dominant opaque body colour.</summary>
public sealed record PanelSpec(int R, int G, int B, bool Semi, int Tpage, int Clut, string Path, int W, int H);
/// <summary>The talk-box contract: the seven frame PNGs, the four layouts and the panel.</summary>
public sealed record DialogueFrameData(IReadOnlyDictionary<int, DialogueFrame> Frames, IReadOnlyList<TalkBoxLayout> Layouts, PanelSpec Panel);
