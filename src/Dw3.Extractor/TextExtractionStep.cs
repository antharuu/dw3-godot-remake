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
    private const string TextDomain = "text";

    // SLES_039.36 disc path (EU disc, constant).
    private const string SlesDiscPath = "SLES_039.36";
    // CMFONTTM.BIN disc path.
    private const string CmfonttmDiscPath = "AAA/DAT/COUNTRY/CMFONTTM.BIN";
    // French talk text file paths: FSTALK00.BIN .. FSTALK09.BIN.
    private const int TalkFileCount = 10;
    private static string TalkDiscPath(int index) => $"AAA/DAT/COUNTRY/FRA/FSTALK{index:D2}.BIN";

    // Atlas palette count (matches Python decoder FONT_CLUT_ROWS=8).
    private const int PaletteCount = 8;
    // Applied palette index for legacy dark-blue rendering.
    private const int LegacyPaletteIndex = 2;
    // Number of atlas pages per font (both pages from CMFONTTM).
    private const int PageCount = 2;

    // Style IDs for glyph token code: glyphs start at 0x04, icons at 0x01.
    private const int GlyphCodeBase = 4;
    private const int IconCodeBase = 1;

    internal static int Extract(string cuePath, string outputDirectory, string domain, TextWriter output, TextWriter error)
    {
        try
        {
            using var disc = DiscSource.Open(cuePath);
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

    private static void ExtractFont(
        DiscSource disc,
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

    private static void InsertFontStylesToDb(DataPackWriteContext context, IReadOnlyList<FontStyleMetrics> styles)
    {
        for (int i = 0; i < styles.Count; i++)
        {
            int styleId = i + 1;
            FontStyleMetrics style = styles[i];
            Insert(context, "INSERT INTO font_styles (style_id, line_height, glyph_count, icon_count) VALUES ($1,$2,$3,$4)",
                styleId, style.LineHeight, style.Glyphs.Count, style.Icons.Count);
            for (int j = 0; j < style.Glyphs.Count; j++)
            {
                GlyphMetric g = style.Glyphs[j];
                Insert(context, "INSERT INTO font_glyphs (style_id,code,page,u,v,clut_x,clut_y,width,height,dx,dy,advance) VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12)",
                    styleId, j + GlyphCodeBase, g.Page, g.U, g.V, g.ClutX, g.ClutY, g.Width, g.Height, (int)g.Dx, (int)g.Dy, g.Advance);
            }

            for (int j = 0; j < style.Icons.Count; j++)
            {
                GlyphMetric ic = style.Icons[j];
                Insert(context, "INSERT INTO font_icons (style_id,code,page,u,v,clut_x,clut_y,width,height,dx,dy,advance) VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12)",
                    styleId, j + IconCodeBase, ic.Page, ic.U, ic.V, ic.ClutX, ic.ClutY, ic.Width, ic.Height, (int)ic.Dx, (int)ic.Dy, ic.Advance);
            }
        }
    }

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

    private static void WriteFontMetricsJson(DataPackWriteContext context, IReadOnlyList<FontStyleMetrics> styles)
    {
        // Build the font-metrics.json payload (mirrors the Python decoder's schema).
        string metricsPath = context.GetImagePath("font/font-metrics.json");
        using var metricsWriter = new Utf8JsonWriter(File.Create(metricsPath), new JsonWriterOptions { Indented = true });
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

    private static void WriteFontClutJson(DataPackWriteContext context, FontAtlas atlas, string cmfonttmSha256)
    {
        // Build the font-clut.json payload.
        string clutPath = context.GetImagePath("font/font-clut.json");
        using var clutWriter = new Utf8JsonWriter(File.Create(clutPath), new JsonWriterOptions { Indented = true });
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

    private static void ExtractText(
        DiscSource disc,
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
            Insert(context, "INSERT INTO text_files (file_index, disc_path, entry_count) VALUES ($1,$2,$3)",
                fileIndex, discPath, tableEntries.Count);

            // Write token JSON.
            string jsonPath = $"text/talk{fileIndex:D2}.json";
            string fullPath = context.GetImagePath(jsonPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

            using var jsonWriter = new Utf8JsonWriter(File.Create(fullPath), new JsonWriterOptions { Indented = false });
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

    private static void WriteTokensJson(Utf8JsonWriter jsonWriter, IReadOnlyList<IReadOnlyList<TextToken>> tableEntries)
    {
        foreach (var entryTokens in tableEntries)
        {
            jsonWriter.WriteStartArray();
            WriteEntryTokens(jsonWriter, entryTokens);
            jsonWriter.WriteEndArray();
        }
    }

    private static void WriteEntryTokens(Utf8JsonWriter jsonWriter, IReadOnlyList<TextToken> tokens)
    {
        foreach (TextToken token in tokens)
        {
            jsonWriter.WriteStartObject();
            WriteTokenObject(jsonWriter, token);
            jsonWriter.WriteEndObject();
        }
    }

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

    private static DiscFileEntry Require(IReadOnlyList<DiscFileEntry> entries, string path) =>
        entries.SingleOrDefault(e => string.Equals(e.IsoPath, path, StringComparison.Ordinal))
            ?? throw new InvalidDataException($"Required disc file is absent: {path}");

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
