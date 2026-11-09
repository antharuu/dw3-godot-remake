using Dw3.Data;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Dw3.Extractor.Tests;

/// <summary>
/// The full extraction runs its domains into scratch packs and merges them into one pack. These
/// tests drive it with fabricated domains that write small packs, so the merge, the progress
/// protocol and the scratch clean-up are checked without any decoder.
/// </summary>
public sealed class FullExtractionStepTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"dw3-full-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void MergesEveryDomainPackIntoOnePackStampedWithTheEdition()
    {
        var disc = new FabricatedDisc();
        using var output = new StringWriter();
        ExtractionDomain[] domains =
        [
            ZoneDomain("first", stage: 7, width: 128, image: "zones/S007/floor.png", extraFile: "zones/S007/manifest.json"),
            ZoneDomain("second", stage: 9, width: 256, image: null, extraFile: null),
        ];

        int exitCode = FullExtractionStep.Extract(disc.Open, _root, output, domains);

        Assert.Equal(0, exitCode);
        Assert.Equal(
            [
                """{"step":"first","done":1,"total":2}""",
                """{"step":"second","done":2,"total":2}""",
                """{"event":"complete","domains":2}""",
            ],
            output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
        using SqliteConnection connection = new DataPackReader(_root).OpenDatabase();
        Assert.Equal("7:128,9:256", Query(connection, "SELECT group_concat(stage_id || ':' || width_pixels, ',') FROM (SELECT * FROM zones ORDER BY stage_id)"));
        Assert.Equal("zones/S007/floor.png", Query(connection, "SELECT group_concat(relative_path) FROM pack_images"));
        Assert.Equal(FabricatedDisc.Edition.BinSha1, Query(connection, "SELECT disc_sha1 FROM meta"));
        Assert.Equal(FabricatedDisc.Edition.Label, Query(connection, "SELECT edition FROM meta"));
        Assert.Equal([1, 2, 3], File.ReadAllBytes(Path.Combine(_root, "data", "zones", "S007", "floor.png")));
        Assert.Equal("{}", File.ReadAllText(Path.Combine(_root, "data", "zones", "S007", "manifest.json")));
        Assert.Empty(Directory.GetDirectories(_root, ".extracting-*"));
        Assert.Equal(0, disc.OpenHandles);
    }

    [Fact]
    public void LaterDomainRowReplacesAnEarlierRowWithTheSameKey()
    {
        var disc = new FabricatedDisc();

        int exitCode = FullExtractionStep.Extract(
            disc.Open,
            _root,
            TextWriter.Null,
            [ZoneDomain("first", 4, 128, null, null), ZoneDomain("second", 4, 384, null, null)]);

        Assert.Equal(0, exitCode);
        using SqliteConnection connection = new DataPackReader(_root).OpenDatabase();
        Assert.Equal("384", Query(connection, "SELECT width_pixels FROM zones WHERE stage_id = 4"));
    }

    [Fact]
    public void StopsAtTheFirstFailingDomainAndKeepsItsExitCode()
    {
        var disc = new FabricatedDisc();
        using var output = new StringWriter();
        bool laterDomainRan = false;
        ExtractionDomain[] domains =
        [
            ZoneDomain("first", 1, 128, null, null),
            new("broken", (_, _) => 3),
            new("later", (_, _) =>
            {
                laterDomainRan = true;
                return 0;
            }),
        ];

        int exitCode = FullExtractionStep.Extract(disc.Open, _root, output, domains);

        Assert.Equal(3, exitCode);
        Assert.False(laterDomainRan);
        Assert.EndsWith(
            """{"event":"error","code":"DOMAIN_EXTRACTION_FAILED","message":"The broken extraction step failed with exit code 3."}""" + Environment.NewLine,
            output.ToString());
        Assert.False(File.Exists(Path.Combine(_root, "data", "dw3.sqlite")));
        Assert.Empty(Directory.GetDirectories(_root, ".extracting-*"));
    }

    [Fact]
    public void DomainThatWritesNoPackFailsTheMerge()
    {
        var disc = new FabricatedDisc();
        using var output = new StringWriter();

        int exitCode = FullExtractionStep.Extract(disc.Open, _root, output, [new("silent", (_, _) => 0)]);

        Assert.Equal(1, exitCode);
        Assert.Contains("EXTRACTION_FAILED", output.ToString());
        Assert.Contains("The silent extraction step did not produce a data pack.", output.ToString());
        Assert.False(File.Exists(Path.Combine(_root, "data", "dw3.sqlite")));
    }

    [Fact]
    public void RegisteredImageMissingFromTheDomainPackFailsTheMerge()
    {
        var disc = new FabricatedDisc();
        using var output = new StringWriter();
        ExtractionDomain domain = new("lossy", (openDisc, directory) =>
        {
            int code = ZoneDomain("lossy", 2, 128, "zones/S002/gone.png", null).Run(openDisc, directory);
            File.Delete(Path.Combine(directory, "data", "zones", "S002", "gone.png"));
            return code;
        });

        int exitCode = FullExtractionStep.Extract(disc.Open, _root, output, [domain]);

        Assert.Equal(1, exitCode);
        Assert.Contains("Domain pack image is missing: zones/S002/gone.png.", output.ToString());
    }

    [Fact]
    public void UnsupportedEditionFailsAfterTheDomainsWithoutWritingThePack()
    {
        var disc = new FabricatedDisc { Unsupported = true };
        using var output = new StringWriter();

        int exitCode = FullExtractionStep.Extract(disc.Open, _root, output, [new("noop", (_, _) => 0)]);

        Assert.Equal(1, exitCode);
        Assert.Contains("\"code\":\"EXTRACTION_FAILED\"", output.ToString());
        Assert.False(File.Exists(Path.Combine(_root, "data", "dw3.sqlite")));
        Assert.Equal(0, disc.OpenHandles);
    }

    [Fact]
    public void TheProductionPipelineRunsTwentyDomainsInAFixedOrder()
    {
        Assert.Equal(
            [
                "zones", "collision", "player", "water", "climb", "drop", "cue", "icon", "npc", "text", "font",
                "dialogue", "talk-box", "encounter", "battle", "digivolution", "battle-models", "battle-stages",
                "battle-hud", "battle-audio", "battle-report",
            ],
            FullExtractionStep.Domains.Select(domain => domain.Name));
    }

    [Fact]
    public void FullCliExtractionOnAnEmptyDiscReportsTheFirstDomainFailureAsJson()
    {
        var disc = new FabricatedDisc();
        using var output = new StringWriter();

        int exitCode = Cli.Run(["extract", "--disc", "fabricated.cue", "--out", _root, "--progress", "jsonl"], output, TextWriter.Null, _ => disc.Open());

        Assert.Equal(1, exitCode);
        Assert.Equal(
            """{"event":"error","code":"DOMAIN_EXTRACTION_FAILED","message":"The zones extraction step failed with exit code 1."}""" + Environment.NewLine,
            output.ToString());
        Assert.Equal(0, disc.OpenHandles);
    }

    private static ExtractionDomain ZoneDomain(string name, int stage, int width, string? image, string? extraFile) =>
        new(name, (openDisc, directory) =>
        {
            using var disc = openDisc();
            new DataPackWriter(directory).Write(
                new DataPackMetadata("0.0.1", disc.IdentifyEdition().BinSha1, name, DateTimeOffset.UnixEpoch),
                context =>
                {
                    using SqliteCommand command = context.Database.CreateCommand();
                    command.Transaction = context.Transaction;
                    command.CommandText = "INSERT INTO zones VALUES ($stage, 1, 1, $width, 128)";
                    command.Parameters.AddWithValue("$stage", stage);
                    command.Parameters.AddWithValue("$width", width);
                    command.ExecuteNonQuery();
                    if (image is not null)
                    {
                        WriteFile(context.GetImagePath(image), [1, 2, 3]);
                        context.RegisterImage(image);
                    }

                    if (extraFile is not null)
                    {
                        File.WriteAllText(CreateParent(context.GetImagePath(extraFile)), "{}");
                    }
                });
            return 0;
        });

    private static void WriteFile(string path, byte[] content) => File.WriteAllBytes(CreateParent(path), content);

    private static string CreateParent(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return path;
    }

    private static string? Query(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }
}
