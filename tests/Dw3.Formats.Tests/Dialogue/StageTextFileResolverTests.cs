using Dw3.Formats.Binary;
using Dw3.Formats.Dialogue;
using Xunit;

namespace Dw3.Formats.Tests.Dialogue;

public sealed class StageTextFileResolverTests
{
    [Fact]
    public void PicksTheCopyInTheCountryFolderAndReadsItsTalkNumber()
    {
        StageTextFileResolver resolver = Resolver(
            (5, "AAA/DAT/COUNTRY/ABC/ASTALK07.BIN"), (6, "AAA/DAT/COUNTRY/XYZ/XSTALK07.BIN"), (7, "AAA/DAT/COUNTRY/DEF/DSTALK07.BIN"));

        Assert.Equal(new StageTextFile(5, 6, "AAA/DAT/COUNTRY/XYZ/XSTALK07.BIN", 7), resolver.Resolve(5));
    }

    [Fact]
    public void RejectsSelectorWithoutCopyInTheFolder() =>
        Assert.Throws<FormatException>(() => Resolver((5, "AAA/DAT/COUNTRY/ABC/ASTALK07.BIN")).Resolve(5));

    [Fact]
    public void RejectsSelectorWithSeveralCopiesInTheFolder() =>
        Assert.Throws<FormatException>(() => Resolver(
            (5, "AAA/DAT/COUNTRY/XYZ/XSTALK07.BIN"), (6, "AAA/DAT/COUNTRY/XYZ/XSTALK08.BIN")).Resolve(5));

    [Fact]
    public void RejectsMemberThatIsNotATalkFile() =>
        Assert.Throws<FormatException>(() => Resolver((5, "AAA/DAT/COUNTRY/XYZ/XSSYSTEM.BIN")).Resolve(5));

    [Theory]
    [InlineData(-1)]
    [InlineData(14)]
    public void RejectsSelectorOutsideTheFileTable(int selector) =>
        Assert.Throws<FormatException>(() => Resolver().Resolve(selector));

    private static StageTextFileResolver Resolver(params (int FileId, string Path)[] files)
    {
        byte[] exe = DialogueFixtures.Executable(0x200);
        var paths = new Dictionary<int, string>();
        foreach ((int fileId, string path) in files)
        {
            int lba = 1000 + fileId;
            DialogueFixtures.U32(exe, DialogueFixtures.ExeOffset(DialogueFixtures.Layout.FileSectorsAddress) + (fileId * 4), (uint)lba);
            paths[lba] = path;
        }

        DiscFileTable table = DiscFileTable.Read(
            PsxExecutable.Parse(exe), DialogueFixtures.Layout.FileSectorsAddress, DialogueFixtures.Layout.FileSectorCount, paths);
        return new StageTextFileResolver(table, 3, "XYZ");
    }
}
