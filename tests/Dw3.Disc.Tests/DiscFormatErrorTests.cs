using System.Text;
using Xunit;

namespace Dw3.Disc.Tests;

/// <summary>
/// Error paths of the CUE parser, <see cref="Iso9660Reader"/> and <see cref="DiscIdentity"/>,
/// exercised with the fabricated image of <see cref="SyntheticDiscBuilder"/> patched in memory.
/// No game-derived bytes are present in this file.
/// </summary>
public sealed class DiscFormatErrorTests : IDisposable
{
    private const int PvdUserData = (16 * Iso9660Reader.SectorBytes) + Iso9660Reader.UserDataOffset;
    private const int PathTableUserData = (17 * Iso9660Reader.SectorBytes) + Iso9660Reader.UserDataOffset;
    private const int RootDirectoryUserData = (18 * Iso9660Reader.SectorBytes) + Iso9660Reader.UserDataOffset;

    // The root directory holds ".", ".." (34 bytes each) then the HELLO.TXT record.
    private const int HelloRecord = RootDirectoryUserData + 68;

    private readonly string _tmp = Path.Combine(Path.GetTempPath(), $"dw3-disc-errors-{Guid.NewGuid():N}");

    public DiscFormatErrorTests() => Directory.CreateDirectory(_tmp);

    public void Dispose()
    {
        if (Directory.Exists(_tmp))
        {
            Directory.Delete(_tmp, recursive: true);
        }
    }

    [Fact]
    public void CueAcceptsBareFileNameCommentsAndBlankLines()
    {
        File.WriteAllBytes(Path.Combine(_tmp, "bare.bin"), [0]);
        string cue = WriteCue("bare.cue", "REM fabricated sheet\r\n\r\nFILE bare.bin binary\r\n  TRACK 01 MODE2/2352\r\n");

        using DiscSource disc = DiscSource.Open(cue);
        Assert.Throws<DiscSourceException>(() => disc.ReadVolumeId());
    }

    [Fact]
    public void CueResolvesBinInsideSubdirectoryWithBackslashes()
    {
        Directory.CreateDirectory(Path.Combine(_tmp, "tracks"));
        File.WriteAllBytes(Path.Combine(_tmp, "tracks", "disc.bin"), [0]);
        string cue = WriteCue("nested.cue", "FILE \"tracks\\disc.bin\" BINARY\n");

        File.WriteAllBytes(Path.Combine(_tmp, "tracks", "disc.bin"), SyntheticDiscBuilder.Build());
        using DiscSource disc = DiscSource.Open(cue);
        Assert.Equal("SYNTH", disc.ReadVolumeId());
    }

    [Theory]
    [InlineData("FILE\r\n")]
    [InlineData("FILE \"../outside.bin\" BINARY\r\n")]
    [InlineData("FILE \"/rooted.bin\" BINARY\r\n")]
    [InlineData("FILE \"image.img\" BINARY\r\n")]
    [InlineData("TRACK 01 MODE2/2352\r\n")]
    public void CueRejectsUnsupportedFileDirective(string text)
    {
        string cue = WriteCue("bad.cue", text);

        var exception = Assert.Throws<DiscSourceException>(() => DiscSource.Open(cue));

        Assert.Equal(DiscSourceException.FormatInvalid, exception.ErrorCode);
    }

    [Fact]
    public void CueThatIsADirectoryIsReportedAsUnreadable()
    {
        string directory = Path.Combine(_tmp, "folder.cue");
        Directory.CreateDirectory(directory);

        var exception = Assert.Throws<DiscSourceException>(() => DiscSource.Open(directory));

        Assert.Equal(DiscSourceException.FormatInvalid, exception.ErrorCode);
        Assert.NotNull(exception.InnerException);
    }

    [Fact]
    public void ReadFileContentReturnsNothingForEmptyExtent()
    {
        using var stream = new MemoryStream(SyntheticDiscBuilder.Build());

        Assert.Empty(Iso9660Reader.ReadFileContent(stream, 20, 0));
    }

    [Fact]
    public void ReadFileContentRejectsNegativeLba()
    {
        using var stream = new MemoryStream(SyntheticDiscBuilder.Build());

        var exception = Assert.Throws<DiscSourceException>(() => Iso9660Reader.ReadFileContent(stream, -1, 16));

        Assert.Contains("Negative", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyPathTableIsRejected()
    {
        byte[] image = SyntheticDiscBuilder.Build();
        WriteU32(image, PvdUserData + 132, 0);

        AssertListFilesFails(image, "path table is empty");
    }

    [Fact]
    public void TruncatedPathTableEntryIsRejected()
    {
        byte[] image = SyntheticDiscBuilder.Build();
        WriteU32(image, PvdUserData + 132, 5);

        AssertListFilesFails(image, "Truncated ISO9660 path table entry");
    }

    [Fact]
    public void PathTableEntryWithoutNameIsRejected()
    {
        byte[] image = SyntheticDiscBuilder.Build();
        image[PathTableUserData] = 0;

        AssertListFilesFails(image, "bad length");
    }

    [Fact]
    public void PathTableParentAfterTheEntryIsRejected()
    {
        byte[] image = SyntheticDiscBuilder.Build();

        // Second entry starts after the 10-byte root entry, its parent number sits 6 bytes in.
        image[PathTableUserData + 16] = 5;

        AssertListFilesFails(image, "parent reference");
    }

    [Fact]
    public void DirectoryRecordShorterThanTheFixedHeaderIsRejected()
    {
        byte[] image = SyntheticDiscBuilder.Build();
        image[HelloRecord] = 20;

        AssertListFilesFails(image, "length out of range");
    }

    [Fact]
    public void DirectoryRecordNameLongerThanTheRecordIsRejected()
    {
        byte[] image = SyntheticDiscBuilder.Build();
        image[HelloRecord + 32] = 200;

        AssertListFilesFails(image, "name exceeds record");
    }

    [Fact]
    public void DirectorySpanningTwoSectorsSkipsZeroPaddingToTheNextSector()
    {
        byte[] image = SyntheticDiscBuilder.Build();

        // Declare the root directory two sectors long: its tail is zero padding, and the
        // following sector (the SUBDIR records) is read as the root's second sector.
        WriteU32(image, RootDirectoryUserData + 10, 2 * Iso9660Reader.UserDataBytes);
        using var stream = new MemoryStream(image);

        IReadOnlyList<DiscFileEntry> files = Iso9660Reader.ListFiles(stream);

        Assert.Contains(files, file => file.IsoPath == "DATA.BIN");
        Assert.Contains(files, file => file.IsoPath == "HELLO.TXT");
    }

    [Fact]
    public void IdentifyMatchesTheSupportedFingerprintIgnoringCase()
    {
        SupportedEdition known = DiscIdentity.KnownEditions[0];

        SupportedEdition found = DiscIdentity.Identify(known.BinMd5.ToUpperInvariant(), known.BinSha1.ToUpperInvariant());

        Assert.Equal(known, found);
    }

    [Fact]
    public void IdentifyRejectsUnknownFingerprint()
    {
        var exception = Assert.Throws<DiscSourceException>(() => DiscIdentity.Identify("00", "00"));

        Assert.Equal(DiscSourceException.EditionInvalid, exception.ErrorCode);
    }

    [Fact]
    public void DigestsMatchPublishedTestVectors()
    {
        string path = Path.Combine(_tmp, "abc.bin");
        File.WriteAllBytes(path, Encoding.ASCII.GetBytes("abc"));

        (string md5, string sha1, string sha256) = DiscIdentity.DigestFile(path);

        Assert.Equal("900150983cd24fb0d6963f7d28e17f72", md5);
        Assert.Equal("a9993e364706816aba3e25717850c26c9cd0d89d", sha1);
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", sha256);
        Assert.Equal(sha256, DiscIdentity.Sha256Bytes("abc"u8));
    }

    [Fact]
    public void IdentifyEditionRejectsAnImageWithAnotherFingerprint()
    {
        File.WriteAllBytes(Path.Combine(_tmp, "other.bin"), SyntheticDiscBuilder.Build());
        string cue = WriteCue("other.cue", "FILE \"other.bin\" BINARY\r\n");
        using DiscSource disc = DiscSource.Open(cue);

        var exception = Assert.Throws<DiscSourceException>(() => disc.IdentifyEdition());

        Assert.Equal(DiscSourceException.EditionInvalid, exception.ErrorCode);
        disc.Dispose();
    }

    private static void AssertListFilesFails(byte[] image, string expectedMessage)
    {
        using var stream = new MemoryStream(image);

        var exception = Assert.Throws<DiscSourceException>(() => Iso9660Reader.ListFiles(stream));

        Assert.Equal(DiscSourceException.FormatInvalid, exception.ErrorCode);
        Assert.Contains(expectedMessage, exception.Message, StringComparison.Ordinal);
    }

    private static void WriteU32(byte[] buffer, int offset, int value) =>
        BitConverter.GetBytes(value).CopyTo(buffer, offset);

    private string WriteCue(string name, string text)
    {
        string path = Path.Combine(_tmp, name);
        File.WriteAllText(path, text);
        return path;
    }
}
