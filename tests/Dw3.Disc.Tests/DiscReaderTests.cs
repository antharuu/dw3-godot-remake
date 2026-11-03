using Xunit;

namespace Dw3.Disc.Tests;

/// <summary>
/// Tests for <see cref="DiscSource"/>, <see cref="CueParser"/> and <see cref="Iso9660Reader"/>
/// using only synthetic images built entirely in memory.
/// No game-derived bytes are present in this file.
/// </summary>
public sealed class DiscReaderTests : IDisposable
{
    private readonly string _tmp =
        Path.Combine(Path.GetTempPath(), $"dw3-disc-{Guid.NewGuid():N}");

    public DiscReaderTests() => Directory.CreateDirectory(_tmp);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tmp, recursive: true);
        }
        catch
        { /* best-effort */
        }
    }

    // ── CUE parsing ────────────────────────────────────────────────────────
    [Fact]
    public void CueMissing_ThrowsFormatInvalid()
    {
        var ex = Assert.Throws<DiscSourceException>(
            () => DiscSource.Open(Path.Combine(_tmp, "ghost.cue")));
        Assert.Equal(DiscSourceException.FormatInvalid, ex.ErrorCode);
    }

    [Fact]
    public void CueWrongFileType_ThrowsFormatInvalid()
    {
        string cue = Path.Combine(_tmp, "audio.cue");
        File.WriteAllText(cue, "FILE \"test.bin\" AUDIO\r\nTRACK 01 AUDIO\r\n");
        var ex = Assert.Throws<DiscSourceException>(() => DiscSource.Open(cue));
        Assert.Equal(DiscSourceException.FormatInvalid, ex.ErrorCode);
    }

    [Fact]
    public void CueMultipleFiles_ThrowsFormatInvalid()
    {
        string cue = Path.Combine(_tmp, "multi.cue");
        File.WriteAllText(
            cue,
            "FILE \"a.bin\" BINARY\r\nTRACK 01 MODE2/2352\r\n" +
            "FILE \"b.bin\" BINARY\r\nTRACK 02 MODE2/2352\r\n");
        var ex = Assert.Throws<DiscSourceException>(() => DiscSource.Open(cue));
        Assert.Equal(DiscSourceException.FormatInvalid, ex.ErrorCode);
    }

    [Fact]
    public void CueBinAbsent_ThrowsFormatInvalid()
    {
        string cue = Path.Combine(_tmp, "nobin.cue");
        File.WriteAllText(cue, "FILE \"missing.bin\" BINARY\r\nTRACK 01 MODE2/2352\r\n");
        var ex = Assert.Throws<DiscSourceException>(() => DiscSource.Open(cue));
        Assert.Equal(DiscSourceException.FormatInvalid, ex.ErrorCode);
    }

    [Fact]
    public void ValidCue_OpenSucceeds()
    {
        string bin = Path.Combine(_tmp, "present.bin");
        string cue = Path.Combine(_tmp, "present.cue");
        File.WriteAllBytes(bin, new byte[1]);
        File.WriteAllText(cue, "FILE \"present.bin\" BINARY\r\nTRACK 01 MODE2/2352\r\n");

        // Should not throw on Open; the BIN is too small to read, but that's a later error.
        using var disc = DiscSource.Open(cue);
        Assert.NotNull(disc);
    }

    // ── Truncated / malformed BIN ──────────────────────────────────────────
    [Fact]
    public void TruncatedBin_ThrowsFormatInvalid()
    {
        string bin = Path.Combine(_tmp, "tiny.bin");
        string cue = Path.Combine(_tmp, "tiny.cue");
        File.WriteAllBytes(bin, new byte[100]); // far too small
        File.WriteAllText(cue, "FILE \"tiny.bin\" BINARY\r\nTRACK 01 MODE2/2352\r\n");

        using var disc = DiscSource.Open(cue);
        var ex = Assert.Throws<DiscSourceException>(() => disc.ListFiles());
        Assert.Equal(DiscSourceException.FormatInvalid, ex.ErrorCode);
    }

    [Fact]
    public void ZerosBin_ThrowsFormatInvalid()
    {
        string bin = Path.Combine(_tmp, "zeros.bin");
        string cue = Path.Combine(_tmp, "zeros.cue");

        // 20 full-size sectors of zeros: no valid PVD.
        File.WriteAllBytes(bin, new byte[20 * Iso9660Reader.SectorBytes]);
        File.WriteAllText(cue, "FILE \"zeros.bin\" BINARY\r\nTRACK 01 MODE2/2352\r\n");

        using var disc = DiscSource.Open(cue);
        var ex = Assert.Throws<DiscSourceException>(() => disc.ListFiles());
        Assert.Equal(DiscSourceException.FormatInvalid, ex.ErrorCode);
    }

    // ── Synthetic ISO9660 image ────────────────────────────────────────────
    [Fact]
    public void SyntheticDisc_VolumeIdIsSynth()
    {
        string cue = WriteSyntheticDisc();
        using var disc = DiscSource.Open(cue);
        Assert.Equal("SYNTH", disc.ReadVolumeId());
    }

    [Fact]
    public void SyntheticDisc_ListFilesReturnsBothFiles()
    {
        string cue = WriteSyntheticDisc();
        using var disc = DiscSource.Open(cue);
        IReadOnlyList<DiscFileEntry> files = disc.ListFiles();

        Assert.Equal(2, files.Count);
        Assert.Contains(files, f => f.IsoPath == "HELLO.TXT");
        Assert.Contains(files, f => f.IsoPath == "SUBDIR/DATA.BIN");
    }

    [Fact]
    public void SyntheticDisc_ReadFileReturnsCorrectContent()
    {
        string cue = WriteSyntheticDisc();
        using var disc = DiscSource.Open(cue);
        var files = disc.ListFiles();

        byte[] hello = disc.ReadFile(files.Single(f => f.IsoPath == "HELLO.TXT"));
        byte[] data = disc.ReadFile(files.Single(f => f.IsoPath == "SUBDIR/DATA.BIN"));

        Assert.Equal(SyntheticDiscBuilder.HelloContent, hello);
        Assert.Equal(SyntheticDiscBuilder.DataContent, data);
    }

    [Fact]
    public void SyntheticDisc_IdentifyEditionThrowsEditionInvalid()
    {
        string cue = WriteSyntheticDisc();
        using var disc = DiscSource.Open(cue);
        var ex = Assert.Throws<DiscSourceException>(() => disc.IdentifyEdition());

        // The synthetic disc has wrong fingerprints — edition mismatch.
        Assert.Equal(DiscSourceException.EditionInvalid, ex.ErrorCode);
    }

    // ── Helpers ────────────────────────────────────────────────────────────
    private string WriteSyntheticDisc()
    {
        byte[] binBytes = SyntheticDiscBuilder.Build();
        string bin = Path.Combine(_tmp, "synthetic.bin");
        string cue = Path.Combine(_tmp, "synthetic.cue");
        File.WriteAllBytes(bin, binBytes);
        File.WriteAllText(cue, "FILE \"synthetic.bin\" BINARY\r\nTRACK 01 MODE2/2352\r\n  INDEX 01 00:00:00\r\n");
        return cue;
    }
}
