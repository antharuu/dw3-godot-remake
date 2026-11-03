using Dw3.Data;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Dw3.Extractor.Tests;

/// <summary>
/// Runs the field steps that read fixed disc members (climb and drop actions, collision actors,
/// talk box) on fabricated discs.
/// </summary>
public sealed class FieldMemberStepsTests : IDisposable
{
    private const string ClimbArchive = "AAA/DAT/FIELD/SPRT/P002PLYL.BIN";
    private const string DropArchive = "AAA/DAT/FIELD/SPRT/P002PLYD.BIN";

    // The panel CLUT the talk-box decoder reads: texture row 177, halfwords 48 to 63.
    private static readonly (int Row, int Word) PanelClut = (177, 48);

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"dw3-field-members-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void ClimbStepDecodesEachNamedSetAndItsSharedFrame()
    {
        var disc = new FabricatedDisc().Add(ClimbArchive, ActionArchive(lastEntry: 0x17, [0x1A, 0x1B]));
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = Run(disc, "climb", output, error);

        Assert.True(exitCode == 0, error.ToString());
        Assert.Equal("{\"event\":\"domain-complete\",\"domain\":\"climb\",\"sets\":2,\"frames\":1}" + Environment.NewLine, output.ToString());
        using SqliteConnection db = new DataPackReader(_root).OpenDatabase();
        Assert.Equal(1L, Scalar(db, "SELECT count(*) FROM pack_images"));
    }

    [Fact]
    public void DropStepUsesItsOwnArchiveAndSetNames()
    {
        var disc = new FabricatedDisc().Add(DropArchive, ActionArchive(lastEntry: 0x0B, [0x16]));
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = Run(disc, "drop", output, error);

        Assert.True(exitCode == 0, error.ToString());
        Assert.Contains("\"domain\":\"drop\",\"sets\":1,\"frames\":1", output.ToString());
    }

    [Fact]
    public void ActionSetUnknownToTheDiscLayoutIsRejected()
    {
        var disc = new FabricatedDisc().Add(DropArchive, ActionArchive(lastEntry: 0x0B, [0x30]));
        using var error = new StringWriter();

        int exitCode = Run(disc, "drop", TextWriter.Null, error);

        Assert.Equal(1, exitCode);
        Assert.Contains("drop action set 30 is not identified by its disc layout", error.ToString());
    }

    [Fact]
    public void ActionArchiveWithoutSetsIsRejected()
    {
        var disc = new FabricatedDisc().Add(DropArchive, ActionArchive(lastEntry: 0x0B, []));
        using var error = new StringWriter();

        int exitCode = Run(disc, "drop", TextWriter.Null, error);

        Assert.Equal(1, exitCode);
        Assert.Contains("drop action archive contains no animation sets", error.ToString());
    }

    [Fact]
    public void ActionSetMappingDirectionsToDifferentSequencesIsRejected()
    {
        byte[] archive = ActionArchive(lastEntry: 0x0B, [0x16]);
        int table = BitConverter.ToInt32(archive, 0x0B * 4);
        Fabricate.U16(archive, table + 4, 2);
        var disc = new FabricatedDisc().Add(DropArchive, archive);
        using var error = new StringWriter();

        int exitCode = Run(disc, "drop", TextWriter.Null, error);

        Assert.Equal(1, exitCode);
        Assert.Contains("drop action set 16 maps directions to different sequences", error.ToString());
    }

    [Fact]
    public void CollisionStepRejectsAnActorWithAnUnknownWidthId()
    {
        var disc = new FabricatedDisc()
            .Add("AAA/PRO/WSTAG200.PRO", new byte[0x2000])
            .Add("AAA/PRO/WSTAG235.PRO", new byte[0x2000]);
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = Run(disc, "collision", output, error);

        Assert.Equal(1, exitCode);
        Assert.Contains("Unsupported actor width ID 0x00", error.ToString());
        Assert.Empty(output.ToString());
    }

    [Fact]
    public void CollisionStepNamesTheStageWhoseOverlayIsMissing()
    {
        var disc = new FabricatedDisc().Add("AAA/PRO/WSTAG235.PRO", new byte[0x2000]);
        using var error = new StringWriter();

        int exitCode = Run(disc, "collision", TextWriter.Null, error);

        Assert.Equal(1, exitCode);
        Assert.Contains("Required disc file missing for S200: AAA/PRO/WSTAG200.PRO", error.ToString());
    }

    [Fact]
    public void TalkBoxStepRendersSevenFramesThePanelAndFourLayouts()
    {
        var disc = new FabricatedDisc()
            .Add("SLES_039.36", Fabricate.Executable(0x80050000))
            .Add("AAA/DAT/COUNTRY/CMFONTCS.BIN", FabricatedSprites.Sheet([0, 1, 2, 3, 4, 5, 6, 7]))
            .Add("AAA/DAT/COUNTRY/CMFONTTM.BIN", FabricatedSprites.MenuTexture(PanelClut));
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = Run(disc, "talk-box", output, error);

        Assert.True(exitCode == 0, error.ToString());
        Assert.Equal("{\"event\":\"domain-complete\",\"domain\":\"talk-box\",\"frames\":7,\"layouts\":4}" + Environment.NewLine, output.ToString());
    }

    /// <summary>
    /// Builds an action archive with <paramref name="lastEntry"/> + 1 entries: entry 0 is a 2×1 direct-16
    /// TIM, entry 1 the only sequence (one 4-tick record of frame 0 at (-1, -2), then -1), entries 2 to
    /// lastEntry - 1 four-byte fillers, and the last entry the set table mapping every set to sequence 1.
    /// </summary>
    private static byte[] ActionArchive(int lastEntry, IReadOnlyList<int> setIds)
    {
        var entries = new List<byte[]> { Fabricate.DirectTim(), Fabricate.Words(4, 0, -1, -2, -1) };
        while (entries.Count < lastEntry)
        {
            entries.Add(new byte[4]);
        }

        entries.Add(Fabricate.SetTable(setIds, sequence: 1));
        return Fabricate.Archive(entries);
    }

    private static object? Scalar(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    private int Run(FabricatedDisc disc, string domain, TextWriter output, TextWriter error) =>
        Cli.Run(["extract", "--disc", "x.cue", "--out", _root, "--only", domain], output, error, _ => disc.Open());
}
