using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Dw3.Data;
using Dw3.Disc;
using Dw3.Formats.Binary.Png;
using Dw3.Formats.Text;
using Microsoft.Data.Sqlite;

namespace Dw3.Extractor;

/// <summary>
/// Extracts font metrics and field text tables from the supported disc.
///
/// Domain "font":  reads SLES_039.36 (TextStyle table) and AAA/DAT/COUNTRY/CMFONTTM.BIN
///   (font atlas). Writes font-metrics.json, font-clut.json, and per-style per-palette
///   PNG atlas pages under font/ in the data pack.
///
/// Domain "text":  reads AAA/DAT/COUNTRY/FRA/FSTALK{00..09}.BIN (field text tables).
///   Writes talk{00..09}.json under text/ in the data pack.
/// </summary>
internal static class TextExtractionStep
{
    private const string FontDomain = "font";

    // SLES_039.36 disc path (EU disc, constant).
    private const string SlesDiscPath = "SLES_039.36";

    // CMFONTTM.BIN disc path.
    private const string CmfonttmDiscPath = "AAA/DAT/COUNTRY/CMFONTTM.BIN";

    // French talk text file paths: FSTALK00.BIN .. FSTALK09.BIN.
    private const int TalkFileCount = 10;

    // Atlas palette count (matches Python decoder FONT_CLUT_ROWS=8).
    private const int PaletteCount = 8;

    // Applied palette index for legacy dark-blue rendering.
    private const int LegacyPaletteIndex = 2;

    // Number of atlas pages per font (both pages from CMFONTTM).
    private const int PageCount = 2;

    // Style IDs for glyph token code: glyphs start at 0x04, icons at 0x01.
    private const int GlyphCodeBase = 4;
    private const int IconCodeBase = 1;

    /// <summary>
    /// Returns the disc path for the French field text file at <paramref name="index"/>:
    /// <c>AAA/DAT/COUNTRY/FRA/FSTALKnn.BIN</c> where <c>nn</c> is the zero-padded index.
    /// </summary>
    /// <param name="index">File index in the range 0..9.</param>
    /// <returns>ISO 9660 disc path for the corresponding FSTALK file.</returns>
    private static string TalkDiscPath(int index) => $"AAA/DAT/COUNTRY/FRA/FSTALK{index:D2}.BIN";

    /// <summary>
    /// Opens the disc through <paramref name="openDisc"/> and dispatches to either
    /// <c>ExtractFont</c> or <c>ExtractText</c> depending on <paramref name="domain"/>.
    /// </summary>
    /// <param name="openDisc">Opens the disc that holds the text tables and font files; disposed before returning.</param>
    /// <param name="outputDirectory">Root directory for the written data pack.</param>
    /// <param name="domain">Either <c>"font"</c> or <c>"text"</c>; determines which extraction path to take.</param>
    /// <param name="output">Receives JSON progress lines on success.</param>
    /// <param name="error">Receives a plain-text error message on failure.</param>
    /// <returns>0 on success, 1 on any disc or decoding error.</returns>
    internal static int Extract(Func<IDiscSource> openDisc, string outputDirectory, string domain, TextWriter output, TextWriter error)
    {
        try
        {
            using var disc = openDisc();
            SupportedEdition edition = disc.IdentifyEdition();
            IReadOnlyList<DiscFileEntry> entries = disc.ListFiles();

            new DataPackWriter(outputDirectory).Write(
                new DataPackMetadata(
                    typeof(TextExtractionStep).Assembly.GetName().Version?.ToString(3) ?? "0.1.0",
                    edition.BinSha1,
                    edition.Label,
                    DateTimeOffset.UtcNow),
                context =>
                {
                    if (string.Equals(domain, FontDomain, StringComparison.Ordinal))
                    {
                        ExtractFont(disc, entries, context, output);
                    }
                    else
                    {
                        ExtractText(disc, entries, context, output);
                    }
                });
            return 0;
        }
        catch (Exception exception) when (exception is DiscSourceException or InvalidDataException or FormatException or IOException or ArgumentException)
        {
            error.WriteLine($"Extraction error ({domain}): {exception.Message}");
            return 1;
        }
    }

    // ── Font ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads SLES_039.36 for font style descriptors and CMFONTTM.BIN for the atlas, then writes
    /// per-style per-palette PNG pages, legacy dark-blue pages for the dialogue renderer,
    /// <c>font/font-metrics.json</c>, and <c>font/font-clut.json</c> to the data pack.
    /// </summary>
    /// <param name="disc">Open disc source for reading raw files.</param>
    /// <param name="entries">Flat list of disc file entries from <see cref="Dw3.Disc.DiscSource.ListFiles"/>.</param>
    /// <param name="context">Write context for the destination data pack.</param>
    /// <param name="output">Receives one JSON progress line on completion.</param>
    private static void ExtractFont(
        IDiscSource disc,
        IReadOnlyList<DiscFileEntry> entries,
        DataPackWriteContext context,
        TextWriter output)
    {
        // Read SLES_039.36 for font style descriptors.
        byte[] sles = disc.ReadFile(Require(entries, SlesDiscPath));
        IReadOnlyList<FontStyleMetrics> styles = FontStyleDecoder.Decode(sles);

        // Read CMFONTTM.BIN for the atlas.
        byte[] cmfonttm = disc.ReadFile(Require(entries, CmfonttmDiscPath));
        string cmfonttmSha256 = Convert.ToHexStringLower(SHA256.HashData(cmfonttm));
        FontAtlas atlas = FontAtlasDecoder.Decode(cmfonttm, cmfonttmSha256);

        InsertFontStylesToDb(context, styles);
        WriteAtlasPngPages(context, styles, atlas);
        WriteLegacyAtlasPngPages(context, atlas);
        WriteFontMetricsJson(context, styles);
        WriteFontClutJson(context, atlas, cmfonttmSha256);

        int totalGlyphs = styles.Sum(s => s.Glyphs.Count);
        int totalIcons = styles.Sum(s => s.Icons.Count);
        output.WriteLine($"{{\"event\":\"domain-complete\",\"domain\":\"font\",\"styles\":{styles.Count},\"glyphs\":{totalGlyphs},\"icons\":{totalIcons}}}");
    }

    /// <summary>
    /// Inserts one row per style into <c>font_styles</c>, one per glyph into <c>font_glyphs</c>,
    /// and one per icon into <c>font_icons</c>. Glyph codes start at <see cref="GlyphCodeBase"/> (4),
    /// icon codes at <see cref="IconCodeBase"/> (1).
    /// </summary>
    /// <param name="context">Write context for the destination data pack.</param>
    /// <param name="styles">Decoded font style metrics in style-index order (style id = index + 1).</param>
    private static void InsertFontStylesToDb(DataPackWriteContext context, IReadOnlyList<FontStyleMetrics> styles)
    {
        for (int i = 0; i < styles.Count; i++)
        {
            int styleId = i + 1;
            FontStyleMetrics style = styles[i];
            Insert(
                context,
                "INSERT INTO font_styles (style_id, line_height, glyph_count, icon_count) VALUES ($1,$2,$3,$4)",
                styleId,
                style.LineHeight,
                style.Glyphs.Count,
                style.Icons.Count);
            for (int j = 0; j < style.Glyphs.Count; j++)
            {
                GlyphMetric g = style.Glyphs[j];
                Insert(
                    context,
                    "INSERT INTO font_glyphs (style_id,code,page,u,v,clut_x,clut_y,width,height,dx,dy,advance) VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12)",
                    styleId,
                    j + GlyphCodeBase,
                    g.Page,
                    g.U,
                    g.V,
                    g.ClutX,
                    g.ClutY,
                    g.Width,
                    g.Height,
                    (int)g.Dx,
                    (int)g.Dy,
                    g.Advance);
            }

            for (int j = 0; j < style.Icons.Count; j++)
            {
                GlyphMetric ic = style.Icons[j];
                Insert(
                    context,
                    "INSERT INTO font_icons (style_id,code,page,u,v,clut_x,clut_y,width,height,dx,dy,advance) VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12)",
                    styleId,
                    j + IconCodeBase,
                    ic.Page,
                    ic.U,
                    ic.V,
                    ic.ClutX,
                    ic.ClutY,
                    ic.Width,
                    ic.Height,
                    (int)ic.Dx,
                    (int)ic.Dy,
                    ic.Advance);
            }
        }
    }

    /// <summary>
    /// Writes per-style per-palette atlas PNG pages to <c>font/font-style-S-palette-P-page-G.png</c>
    /// in the data pack image tree, covering all styles, all eight CLUT palettes, and both atlas pages.
    /// </summary>
    /// <param name="context">Write context for the destination data pack.</param>
    /// <param name="styles">Decoded font style metrics (determines the outer style loop count).</param>
    /// <param name="atlas">Decoded font atlas providing page pixel data and CLUT palettes.</param>
    private static void WriteAtlasPngPages(DataPackWriteContext context, IReadOnlyList<FontStyleMetrics> styles, FontAtlas atlas)
    {
        // Write per-style per-palette PNG atlas pages.
        for (int styleIdx = 0; styleIdx < styles.Count; styleIdx++)
        {
            int styleId = styleIdx + 1;
            for (int paletteIdx = 0; paletteIdx < PaletteCount; paletteIdx++)
            {
                for (int pageIdx = 0; pageIdx < PageCount && pageIdx < atlas.Pages.Count; pageIdx++)
                {
                    string pngPath = $"font/font-style-{styleId}-palette-{paletteIdx}-page-{pageIdx}.png";
                    FontAtlasPage page = atlas.Pages[pageIdx];
                    byte[] rgba = FontAtlasDecoder.RenderPage(page, atlas.Palettes[paletteIdx]);
                    byte[] png = PngEncoder.Encode(page.Width, page.Height, rgba);
                    string fullPath = context.GetImagePath(pngPath);
                    Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                    File.WriteAllBytes(fullPath, png);
                    context.RegisterImage(pngPath);
                }
            }
        }
    }

    /// <summary>
    /// Writes the two legacy dark-blue atlas pages used by the existing dialogue renderer
    /// (style 1 only, CLUT palette index 2) to <c>font/font-style-1-page-N.png</c>.
    /// These pages are preserved for backward compatibility with the dialogue renderer's fixed palette.
    /// </summary>
    /// <param name="context">Write context for the destination data pack.</param>
    /// <param name="atlas">Decoded font atlas providing the page pixel data and CLUT palettes.</param>
    private static void WriteLegacyAtlasPngPages(DataPackWriteContext context, FontAtlas atlas)
    {
        // Legacy dark-blue pages for the existing dialogue renderer (style 1 only).
        for (int pageIdx = 0; pageIdx < PageCount && pageIdx < atlas.Pages.Count; pageIdx++)
        {
            string pngPath = $"font/font-style-1-page-{pageIdx}.png";
            FontAtlasPage page = atlas.Pages[pageIdx];
            byte[] rgba = FontAtlasDecoder.RenderPage(page, atlas.Palettes[LegacyPaletteIndex]);
            byte[] png = PngEncoder.Encode(page.Width, page.Height, rgba);
            string fullPath = context.GetImagePath(pngPath);
            File.WriteAllBytes(fullPath, png);
            context.RegisterImage(pngPath);
        }
    }

    /// <summary>
    /// Writes <c>font/font-metrics.json</c> to the data pack: a nested JSON object with a
    /// <c>styles</c> map keyed by style id, each containing <c>lineHeight</c>, <c>glyphs</c>, and <c>icons</c>,
    /// plus legacy top-level <c>glyphs</c> and <c>icons</c> for the dialogue renderer (style 1 only).
    /// </summary>
    /// <param name="context">Write context for the destination data pack.</param>
    /// <param name="styles">Decoded font style metrics in style-index order.</param>
    private static void WriteFontMetricsJson(DataPackWriteContext context, IReadOnlyList<FontStyleMetrics> styles)
    {
        // Build the font-metrics.json payload (mirrors the Python decoder's schema).
        string metricsPath = context.GetImagePath("font/font-metrics.json");
        using FileStream metricsStream = File.Create(metricsPath);
        using var metricsWriter = new Utf8JsonWriter(metricsStream, new JsonWriterOptions { Indented = true });
        metricsWriter.WriteStartObject();
        metricsWriter.WriteString("schema", "m8a-font-metrics-v1");
        metricsWriter.WriteString("source", SlesDiscPath);
        metricsWriter.WriteStartObject("styles");
        for (int i = 0; i < styles.Count; i++)
        {
            int styleId = i + 1;
            FontStyleMetrics style = styles[i];
            metricsWriter.WriteStartObject(styleId.ToString(CultureInfo.InvariantCulture));
            metricsWriter.WriteNumber("lineHeight", style.LineHeight);
            WriteGlyphTable(metricsWriter, "glyphs", style.Glyphs, GlyphCodeBase);
            WriteGlyphTable(metricsWriter, "icons", style.Icons, IconCodeBase);
            metricsWriter.WriteEndObject();
        }

        metricsWriter.WriteEndObject();

        // Legacy top-level glyphs/icons for the existing dialogue renderer (style 1).
        WriteGlyphTable(metricsWriter, "glyphs", styles[0].Glyphs, GlyphCodeBase);
        WriteGlyphTable(metricsWriter, "icons", styles[0].Icons, IconCodeBase);
        metricsWriter.WriteEndObject();
        metricsWriter.Flush();
    }

    /// <summary>
    /// Writes <c>font/font-clut.json</c> to the data pack: applied palette index, CLUT coordinates,
    /// palette page filename template, and all eight palette rows as RGBA arrays.
    /// </summary>
    /// <param name="context">Write context for the destination data pack.</param>
    /// <param name="atlas">Decoded font atlas providing the CLUT palette rows.</param>
    /// <param name="cmfonttmSha256">Lowercase hex SHA-256 of CMFONTTM.BIN, recorded for parity verification.</param>
    private static void WriteFontClutJson(DataPackWriteContext context, FontAtlas atlas, string cmfonttmSha256)
    {
        // Build the font-clut.json payload.
        string clutPath = context.GetImagePath("font/font-clut.json");
        using FileStream clutStream = File.Create(clutPath);
        using var clutWriter = new Utf8JsonWriter(clutStream, new JsonWriterOptions { Indented = true });
        clutWriter.WriteStartObject();
        clutWriter.WriteNumber("applied_palette", LegacyPaletteIndex);
        clutWriter.WriteNumber("clut_x", 48);
        clutWriter.WriteNumber("clut_y", 232);
        clutWriter.WriteString("palette_pages", "font-style-{style}-palette-{palette}-page-{page}.png");
        clutWriter.WriteStartArray("rows");
        foreach (var row in atlas.Palettes)
        {
            clutWriter.WriteStartArray();
            foreach (var (r, g, b, a) in row)
            {
                clutWriter.WriteStartArray();
                clutWriter.WriteNumberValue(r);
                clutWriter.WriteNumberValue(g);
                clutWriter.WriteNumberValue(b);
                clutWriter.WriteNumberValue(a);
                clutWriter.WriteEndArray();
            }

            clutWriter.WriteEndArray();
        }

        clutWriter.WriteEndArray();
        clutWriter.WriteString("schema", "m8a-font-clut-v1");
        clutWriter.WriteString("source", CmfonttmDiscPath);
        clutWriter.WriteString("source_sha256", cmfonttmSha256);
        clutWriter.WriteEndObject();
        clutWriter.Flush();
    }

    /// <summary>
    /// Writes one JSON object property named <paramref name="propertyName"/> containing a map of
    /// code (as string) to a 10-element array <c>[page, u, v, clutX, clutY, w, h, dx, dy, advance]</c>
    /// for each metric in <paramref name="metrics"/>. Codes start at <paramref name="codeBase"/>.
    /// </summary>
    /// <param name="writer">JSON writer positioned inside an enclosing object.</param>
    /// <param name="propertyName">Key for the glyph map property (e.g. <c>"glyphs"</c> or <c>"icons"</c>).</param>
    /// <param name="metrics">Glyph metrics in code order; index 0 maps to code <paramref name="codeBase"/>.</param>
    /// <param name="codeBase">Token code of the first metric (e.g. 4 for glyphs, 1 for icons).</param>
    private static void WriteGlyphTable(Utf8JsonWriter writer, string propertyName, IReadOnlyList<GlyphMetric> metrics, int codeBase)
    {
        writer.WriteStartObject(propertyName);
        for (int i = 0; i < metrics.Count; i++)
        {
            GlyphMetric m = metrics[i];
            writer.WriteStartArray((i + codeBase).ToString(CultureInfo.InvariantCulture));
            writer.WriteNumberValue(m.Page);
            writer.WriteNumberValue(m.U);
            writer.WriteNumberValue(m.V);
            writer.WriteNumberValue(m.ClutX);
            writer.WriteNumberValue(m.ClutY);
            writer.WriteNumberValue(m.Width);
            writer.WriteNumberValue(m.Height);
            writer.WriteNumberValue((int)m.Dx);
            writer.WriteNumberValue((int)m.Dy);
            writer.WriteNumberValue(m.Advance);
            writer.WriteEndArray();
        }

        writer.WriteEndObject();
    }

    // ── Text ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads the ten French field text files (FSTALK00.BIN..FSTALK09.BIN), decodes each as a token
    /// table, inserts a metadata row into <c>text_files</c>, and writes its token JSON to
    /// <c>text/talkNN.json</c> in the data pack image tree.
    /// </summary>
    /// <param name="disc">Open disc source for reading raw files.</param>
    /// <param name="entries">Flat list of disc file entries.</param>
    /// <param name="context">Write context for the destination data pack.</param>
    /// <param name="output">Receives one JSON progress line on completion.</param>
    private static void ExtractText(
        IDiscSource disc,
        IReadOnlyList<DiscFileEntry> entries,
        DataPackWriteContext context,
        TextWriter output)
    {
        int totalEntries = 0;
        for (int fileIndex = 0; fileIndex < TalkFileCount; fileIndex++)
        {
            string discPath = TalkDiscPath(fileIndex);
            byte[] data = disc.ReadFile(Require(entries, discPath));
            IReadOnlyList<IReadOnlyList<TextToken>> tableEntries = TextTableDecoder.DecodeFile(data);

            // Write to SQLite metadata.
            Insert(
                context,
                "INSERT INTO text_files (file_index, disc_path, entry_count) VALUES ($1,$2,$3)",
                fileIndex,
                discPath,
                tableEntries.Count);

            // Write token JSON.
            string jsonPath = $"text/talk{fileIndex:D2}.json";
            string fullPath = context.GetImagePath(jsonPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

            // Utf8JsonWriter does not own the stream: the FileStream is disposed separately so the
            // file handle is released before the next talk file is written.
            using FileStream jsonStream = File.Create(fullPath);
            using var jsonWriter = new Utf8JsonWriter(jsonStream, new JsonWriterOptions { Indented = false });
            jsonWriter.WriteStartObject();
            jsonWriter.WriteNumber("count", tableEntries.Count);
            jsonWriter.WriteStartArray("entries");
            WriteTokensJson(jsonWriter, tableEntries);
            jsonWriter.WriteEndArray();
            jsonWriter.WriteEndObject();
            jsonWriter.Flush();

            totalEntries += tableEntries.Count;
        }

        output.WriteLine($"{{\"event\":\"domain-complete\",\"domain\":\"text\",\"files\":{TalkFileCount},\"total_entries\":{totalEntries}}}");
    }

    /// <summary>
    /// Writes a JSON array of arrays, one per table entry, to <paramref name="jsonWriter"/>.
    /// Each inner array contains the token objects for that entry, serialized by <see cref="WriteEntryTokens"/>.
    /// </summary>
    /// <param name="jsonWriter">JSON writer positioned inside an enclosing <c>entries</c> array.</param>
    /// <param name="tableEntries">All decoded token sequences from one text file.</param>
    private static void WriteTokensJson(Utf8JsonWriter jsonWriter, IReadOnlyList<IReadOnlyList<TextToken>> tableEntries)
    {
        foreach (var entryTokens in tableEntries)
        {
            jsonWriter.WriteStartArray();
            WriteEntryTokens(jsonWriter, entryTokens);
            jsonWriter.WriteEndArray();
        }
    }

    /// <summary>
    /// Writes each token in <paramref name="tokens"/> as a JSON object inside the current array.
    /// Each object is serialized by <see cref="WriteTokenObject"/>.
    /// </summary>
    /// <param name="jsonWriter">JSON writer positioned inside an entry token array.</param>
    /// <param name="tokens">Ordered list of tokens for one text entry.</param>
    private static void WriteEntryTokens(Utf8JsonWriter jsonWriter, IReadOnlyList<TextToken> tokens)
    {
        foreach (TextToken token in tokens)
        {
            jsonWriter.WriteStartObject();
            WriteTokenObject(jsonWriter, token);
            jsonWriter.WriteEndObject();
        }
    }

    /// <summary>
    /// Writes the JSON properties of one <paramref name="token"/> to <paramref name="jsonWriter"/>.
    /// Glyph tokens emit <c>{"t":"g","c":N}</c>, icon tokens <c>{"t":"i","c":N}</c>, and control
    /// tokens <c>{"t":"c","cmd":N,"args":[…]}</c>.
    /// </summary>
    /// <param name="jsonWriter">JSON writer positioned inside the token object (start/end written by the caller).</param>
    /// <param name="token">The decoded text token to serialize.</param>
    private static void WriteTokenObject(Utf8JsonWriter jsonWriter, TextToken token)
    {
        switch (token.Kind)
        {
            case TextTokenKind.Glyph:
                jsonWriter.WriteString("t", "g");
                jsonWriter.WriteNumber("c", token.Code);
                break;
            case TextTokenKind.Icon:
                jsonWriter.WriteString("t", "i");
                jsonWriter.WriteNumber("c", token.Code);
                break;
            case TextTokenKind.Control:
                jsonWriter.WriteString("t", "c");
                jsonWriter.WriteNumber("cmd", token.Code);
                jsonWriter.WriteStartArray("args");
                foreach (int arg in token.Args)
                {
                    jsonWriter.WriteNumberValue(arg);
                }

                jsonWriter.WriteEndArray();
                break;
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the first entry in <paramref name="entries"/> whose ISO path equals <paramref name="path"/>.
    /// </summary>
    /// <param name="entries">Flat list of disc file entries from <see cref="Dw3.Disc.DiscSource.ListFiles"/>.</param>
    /// <param name="path">ISO 9660 path to match (e.g. <c>AAA/DAT/COUNTRY/CMFONTTM.BIN</c>).</param>
    /// <returns>The matching entry.</returns>
    /// <exception cref="InvalidDataException">No entry with the given path was found.</exception>
    private static DiscFileEntry Require(IReadOnlyList<DiscFileEntry> entries, string path) =>
        entries.SingleOrDefault(e => string.Equals(e.IsoPath, path, StringComparison.Ordinal))
            ?? throw new InvalidDataException($"Required disc file is absent: {path}");

    /// <summary>
    /// Executes <paramref name="sql"/> against the destination database within the current transaction,
    /// binding <paramref name="values"/> to positional parameters <c>$1</c>, <c>$2</c>, and so on.
    /// </summary>
    /// <param name="context">Write context providing the database connection and active transaction.</param>
    /// <param name="sql">SQL command text with positional parameters <c>$1</c>, <c>$2</c>, and so on.</param>
    /// <param name="values">Values bound to parameters in order.</param>
    private static void Insert(DataPackWriteContext context, string sql, params object[] values)
    {
        using SqliteCommand cmd = context.Database.CreateCommand();
        cmd.Transaction = context.Transaction;
        cmd.CommandText = sql;
        for (int i = 0; i < values.Length; i++)
        {
            cmd.Parameters.AddWithValue($"${i + 1}", values[i]);
        }

        cmd.ExecuteNonQuery();
    }
}
