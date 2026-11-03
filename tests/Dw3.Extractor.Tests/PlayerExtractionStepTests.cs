using System.Text.Json;
using Dw3.Data;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Dw3.Extractor.Tests;

/// <summary>
/// Runs the player step on a fabricated P002PLAY.BIN: 87 opaque 2×1 frames, one sequence and a set
/// table naming two sets.
/// </summary>
public sealed class PlayerExtractionStepTests : IDisposable
{
    private const string ArchivePath = "AAA/DAT/FIELD/SPRT/P002PLAY.BIN";
    private const int FrameCount = 87;
    private const int SetTableEntry = 112;

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"dw3-player-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void WritesEveryFrameAndAManifestWithContactsAnchorsAndNamedSets()
    {
        var disc = new FabricatedDisc().Add(ArchivePath, PlayerArchive([1, 9]));
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = Run(disc, output, error);

        Assert.True(exitCode == 0, error.ToString());
        Assert.Equal("{\"event\":\"domain-complete\",\"domain\":\"player\",\"frames\":87}" + Environment.NewLine, output.ToString());
        using SqliteConnection db = new DataPackReader(_root).OpenDatabase();
        Assert.Equal((long)FrameCount, Scalar(db, "SELECT count(*) FROM pack_images WHERE relative_path LIKE 'player/frames/frame-%.png'"));
        Assert.True(File.Exists(Path.Combine(_root, "data", "player", "frames", "frame-56.png")));
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(_root, "data", "player", "manifest.json")));
        JsonElement player = manifest.RootElement.GetProperty("player");
        Assert.Equal(FrameCount, player.GetProperty("frames").EnumerateObject().Count());
        JsonElement variant = Assert.Single(player.GetProperty("anchor_variants").EnumerateArray());
        Assert.Equal([-1, -2, 2, 1], variant[0].EnumerateArray().Select(value => value.GetInt32()));
        Assert.Equal(10, variant[1].GetInt32());
        string text = player.GetRawText();
        Assert.Contains("STAND", text);
        Assert.Contains("set_9", text);
        Assert.Contains(ArchivePath, text);
    }

    [Fact]
    public void ArchiveWhoseSetTableHasNoTerminatorIsRejected()
    {
        byte[] archive = PlayerArchive([1]);
        Array.Resize(ref archive, archive.Length - 2);
        var disc = new FabricatedDisc().Add(ArchivePath, archive);
        using var error = new StringWriter();

        int exitCode = Run(disc, TextWriter.Null, error);

        Assert.Equal(1, exitCode);
        Assert.Contains("lacks the required 0x0000 terminator", error.ToString());
    }

    [Fact]
    public void ArchiveTooShortForItsOffsetTableIsRejected()
    {
        var disc = new FabricatedDisc().Add(ArchivePath, new byte[16]);
        using var error = new StringWriter();

        int exitCode = Run(disc, TextWriter.Null, error);

        Assert.Equal(1, exitCode);
        Assert.Contains("Player archive offset table requires 452 bytes", error.ToString());
    }

    [Fact]
    public void PlayerStepCalledForAnotherDomainIsAUsageError()
    {
        var disc = new FabricatedDisc();
        using var error = new StringWriter();

        int exitCode = PlayerExtractionStep.Extract(disc.Open, _root, "zones", TextWriter.Null, error);

        Assert.Equal(2, exitCode);
        Assert.Equal("Unsupported extraction domain: zones" + Environment.NewLine, error.ToString());
        Assert.Equal(0, disc.Opened);
    }

    /// <summary>
    /// Entries 0..86 are frames, entry 87 the only sequence (one 4-tick record of frame 0 at (-1, -2),
    /// then -1), entries 88..111 fillers and entry 112 the set table pointing every direction at 87.
    /// </summary>
    private static byte[] PlayerArchive(IReadOnlyList<int> setIds)
    {
        var entries = new List<byte[]>();
        for (int frame = 0; frame < FrameCount; frame++)
        {
            entries.Add(Fabricate.DirectTim());
        }

        entries.Add(Fabricate.Words(4, 0, -1, -2, -1));
        while (entries.Count < SetTableEntry)
        {
            entries.Add(new byte[4]);
        }

        entries.Add(Fabricate.SetTable(setIds, sequence: FrameCount));
        return Fabricate.Archive(entries);
    }

    private static object? Scalar(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    private int Run(FabricatedDisc disc, TextWriter output, TextWriter error) =>
        Cli.Run(["extract", "--disc", "x.cue", "--out", _root, "--only", "player"], output, error, _ => disc.Open());
}
