using System.Buffers.Binary;
using System.Reflection;
using Dw3.Data;
using Dw3.Formats.Binary.Png;
using Dw3.Formats.Text;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Dw3.Extractor.Tests;

public sealed class FontAtlasPreparationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"dw3-font-palettes-{Guid.NewGuid():N}");

    [Fact]
    public void PreparationUsesEveryStylesClutForAllEightRows()
    {
        var bytes = new byte[64 * 80 * 2];
        byte[] origins = [10, 30, 50];
        foreach (byte origin in origins)
        {
            for (int palette = 0; palette < 8; palette++)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan((((origin + palette) * 64) + 3 + 7) * 2), (ushort)(origin + palette));
            }
        }

        FontStyleMetrics[] styles = [.. origins.Select(origin => new FontStyleMetrics(9, [new GlyphMetric(0, 0, 0, 3, origin, 8, 8, 0, 0, 8)], []))];
        IReadOnlyList<(byte R, byte G, byte B, byte A)>[] dialoguePalettes = [.. Enumerable.Range(0, 8).Select(_ => (IReadOnlyList<(byte R, byte G, byte B, byte A)>)new (byte R, byte G, byte B, byte A)[16])];
        var atlas = new FontAtlas(dialoguePalettes, [new FontAtlasPage(256, 80, bytes), new FontAtlasPage(4, 1, [0x77, 0x77])], "synthetic");
        var metadata = new DataPackMetadata("test", new string('1', 40), "synthetic", DateTimeOffset.UnixEpoch);
        new DataPackWriter(_root).Write(metadata, context => typeof(TextExtractionStep)
            .GetMethod("WriteAtlasPngPages", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [context, styles, atlas]));
        for (int style = 1; style <= 3; style++)
        {
            for (int palette = 0; palette < 8; palette++)
            {
                var (r, g, b, a) = FontAtlasDecoder.HalfwordToRgba((ushort)(origins[style - 1] + palette));
                byte[] rgba = [.. Enumerable.Range(0, 4).SelectMany(_ => new byte[] { r, g, b, a })];
                string path = Path.Combine(_root, "data", "font", $"font-style-{style}-palette-{palette}-page-1.png");
                Assert.Equal(PngEncoder.Encode(4, 1, rgba), File.ReadAllBytes(path));
            }
        }
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }
}
