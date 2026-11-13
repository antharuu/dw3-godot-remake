using Dw3.Data;
using Dw3.Data.Battle;
using Dw3.Formats.Battle;
using Dw3.Formats.Binary.Png;
using Xunit;

namespace Dw3.Extractor.Tests;

public sealed class SpriteEffectExtractionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"dw3-sprites-{Guid.NewGuid():N}");

    [Fact]
    public void ProducerToLoaderPreservesDistinctSheetsAdditivePaletteAndAnimationDurations()
    {
        Extract();
        var index = SpriteEffectDomainReader.Load(new DataPackReader(_root));
        Assert.Equal(2, index.Count);
        var first = SpriteEffectDomainReader.Require(index, 123);
        var second = SpriteEffectDomainReader.Require(index, 456);
        Assert.Equal("available", first.Status);
        Assert.Equal("available", second.Status);
        Assert.Equal(5, first.Payload.GetProperty("sheet").GetProperty("Index").GetInt32());
        Assert.Equal(7, second.Payload.GetProperty("sheet").GetProperty("Index").GetInt32());
        Assert.Equal(1, first.Payload.GetProperty("animations")[0].GetArrayLength());
        Assert.Equal(2, second.Payload.GetProperty("animations")[0].GetArrayLength());
        var frame = first.Payload.GetProperty("frames")[0];
        Assert.Equal(253, frame.GetProperty("row").GetInt32());
        Assert.Equal(2, frame.GetProperty("headerRow").GetInt32());
        Assert.Equal(3, frame.GetProperty("abr").GetInt32());
        Assert.Equal(0x7FFF, frame.GetProperty("parts")[0].GetProperty("palette")[1].GetInt32());
        Assert.Equal(PngEncoder.Encode(2, 1, [255, 255, 255, 255, 255, 255, 255, 255]), File.ReadAllBytes(new DataPackReader(_root).ResolvePath(frame.GetProperty("path").GetString()!)));
        Assert.Throws<InvalidDataException>(() => SpriteEffectDomainReader.Require(index, 999));
        var script = SpriteEffectScriptReader.Load(new DataPackReader(_root)).Single(s => s.GetProperty("selector").GetInt32() == 0);
        Assert.Equal("decoded", script.GetProperty("status").GetString());
        Assert.Equal(12, script.GetProperty("commands")[0].GetProperty("arguments")[1].GetInt32());
        Assert.Equal(1, script.GetProperty("commands")[1].GetProperty("arguments")[0].GetInt32());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LoaderRejectsMissingOrCorruptPngInsteadOfSourceAbsence(bool corrupt)
    {
        Extract();
        string path = Directory.GetFiles(Path.Combine(_root, "data", "battle", "sprites"), "*.png", SearchOption.AllDirectories)[0];
        if (corrupt)
        {
            File.WriteAllBytes(path, [1, 2, 3]);
        }
        else
        {
            File.Delete(path);
        }

        Assert.ThrowsAny<Exception>(() => SpriteEffectDomainReader.Load(new DataPackReader(_root)));
    }

    [Fact]
    public void BoundedEmptyArchiveEntryDiffersFromMalformedIndex()
    {
        byte[] archive = Fabricate.Archive([[1, 2], [3, 4]]);
        Fabricate.U32(archive, 4, 0);
        Assert.Throws<SpriteEffectSourceAbsentException>(() => SpriteEffectSource.Entry(archive, 1));
        Assert.Throws<FormatException>(() => SpriteEffectSource.Entry(archive, 2));
        Assert.Throws<FormatException>(() => SpriteEffectSource.Entry([255, 255, 255, 255], 0));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(51)]
    public void SheetRegistryRejectsOutOfBoundsIdentity(int index)
    {
        Assert.Throws<FormatException>(() => SpriteEffectRegistryDecoder.DecodeSheet(new byte[0x21000], index));
    }

    [Theory]
    [InlineData(-1, "source-absent")]
    [InlineData(255, "unsupported")]
    public void ProducerPreservesQualifiedSourceAbsenceAndExternalPaletteSeparately(int row, string expected)
    {
        Extract(row);
        var result = SpriteEffectDomainReader.Require(SpriteEffectDomainReader.Load(new DataPackReader(_root)), 123);
        Assert.Equal(expected, result.Status);
        Assert.False(string.IsNullOrWhiteSpace(result.Payload.GetProperty("reason").GetString()));
        if (expected == "unsupported")
        {
            Assert.Single(result.Payload.GetProperty("unsupportedFrames").EnumerateArray());
            Assert.Equal(1, result.Payload.GetProperty("animations")[0].GetArrayLength());
            Assert.Equal(256, result.Payload.GetProperty("textureWords")[0].GetArrayLength());
        }
    }

    [Fact]
    public void WrappedClutInNeverWrittenVramResolvesToZeroPaletteAndTransparentLayer()
    {
        // Placement (448, 256), part row 0, header row 2, animated row 255: the packed GPU row is
        // 513 & 0x1FF = 1, column 448, inside the never-written rectangle.
        Extract(255, 448, 256);
        var result = SpriteEffectDomainReader.Require(SpriteEffectDomainReader.Load(new DataPackReader(_root)), 123);
        Assert.Equal("available", result.Status);
        Assert.Empty(result.Payload.GetProperty("unsupportedFrames").EnumerateArray());
        var part = result.Payload.GetProperty("frames")[0].GetProperty("parts")[0];
        Assert.Equal("cleared-vram", part.GetProperty("paletteSource").GetString());
        Assert.Equal([448, 1], part.GetProperty("vram").EnumerateArray().Select(value => value.GetInt32()));
        Assert.All(part.GetProperty("palette").EnumerateArray(), word => Assert.Equal(0, word.GetInt32()));
        string layer = new DataPackReader(_root).ResolvePath(part.GetProperty("layer").GetProperty("path").GetString()!);
        Assert.Equal(PngEncoder.Encode(2, 1, new byte[8]), File.ReadAllBytes(layer));
    }

    [Fact]
    public void InPagePaletteRecordsItsPackedVramAddress()
    {
        Extract(253, 448, 256);
        var part = SpriteEffectDomainReader.Require(SpriteEffectDomainReader.Load(new DataPackReader(_root)), 123)
            .Payload.GetProperty("frames")[0].GetProperty("parts")[0];
        Assert.Equal("effect-page", part.GetProperty("paletteSource").GetString());
        Assert.Equal([448, 511], part.GetProperty("vram").EnumerateArray().Select(value => value.GetInt32()));
        Assert.Equal(0x7FFF, part.GetProperty("palette")[1].GetInt32());
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    private void Extract(int paletteRow = 253, int placementX = 0, int placementY = 0)
    {
        var (disc, _) = FabricatedSpriteEffects.Create(paletteRow, placementX, placementY);
        using var error = new StringWriter();
        int result = Cli.Run(["extract", "--disc", "synthetic.cue", "--out", _root, "--only", "battle-sprites"], TextWriter.Null, error, _ => disc.Open());
        Assert.True(result == 0, error.ToString());
    }
}
