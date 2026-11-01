using Microsoft.Data.Sqlite;
using Xunit;

namespace Dw3.Data.Tests;

/// <summary>
/// Defines the externally observable data-pack lifecycle and validation contract.
/// </summary>
public sealed class DataPackTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"dw3-data-pack-{Guid.NewGuid():N}");

    [Fact]
    public void InterruptedWriteLeavesNoDataDirectory()
    {
        var writer = new DataPackWriter(_root);

        Assert.Throws<InvalidOperationException>(() => writer.Write(CreateMetadata(), _ => throw new InvalidOperationException("interrupted")));

        Assert.False(Directory.Exists(Path.Combine(_root, "data")));
        Assert.Empty(Directory.EnumerateDirectories(_root, "data.staging-*"));
    }

    [Fact]
    public void ReaderRejectsMissingPack()
    {
        Assert.Throws<DataPackMissing>(() => new DataPackReader(_root).OpenDatabase());
    }

    [Fact]
    public void ReaderRejectsPackWithoutDatabase()
    {
        Directory.CreateDirectory(Path.Combine(_root, "data"));

        var exception = Assert.Throws<DataPackIncomplete>(() => new DataPackReader(_root).OpenDatabase());

        Assert.Contains("database", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReaderRejectsDifferentSchemaVersion()
    {
        string data = Path.Combine(_root, "data");
        Directory.CreateDirectory(data);
        using (var connection = new SqliteConnection($"Data Source={Path.Combine(data, "dw3.sqlite")}"))
        {
            connection.Open();
            DataPackSchema.Create(connection);
            using var command = connection.CreateCommand();
            command.CommandText = $"INSERT INTO meta (schema_version, extractor_version, disc_sha1, edition, completed_at) VALUES ({DataPackSchema.Version + 1}, 'test', '0123456789012345678901234567890123456789', 'test-edition', '2026-01-01T00:00:00.0000000+00:00')";
            command.ExecuteNonQuery();
        }

        Assert.Throws<DataPackSchemaMismatch>(() => new DataPackReader(_root).OpenDatabase());
    }

    [Fact]
    public void WriterRemovesOrphanedStagingDirectoriesOnStartup()
    {
        string orphan = Path.Combine(_root, "data.staging-orphan");
        Directory.CreateDirectory(orphan);
        File.WriteAllText(Path.Combine(orphan, "partial"), "discard");

        _ = new DataPackWriter(_root);

        Assert.False(Directory.Exists(orphan));
    }

    [Fact]
    public void ReaderRejectsMissingReferencedImage()
    {
        var writer = new DataPackWriter(_root);
        writer.Write(CreateMetadata(), pack =>
        {
            string imagePath = pack.GetImagePath("images/field/1.png");
            Directory.CreateDirectory(Path.GetDirectoryName(imagePath)!);
            File.WriteAllBytes(imagePath, [0]);
            pack.RegisterImage("images/field/1.png");
        });
        File.Delete(Path.Combine(_root, "data", "images", "field", "1.png"));

        Assert.Throws<DataPackCorrupt>(() => new DataPackReader(_root).OpenDatabase());
    }

    [Fact]
    public void PromotionSucceedsAfterDisposedReaderConnection()
    {
        // Write pack v1 and open a reader, then dispose it.  On Windows, Dispose() returns
        // the connection to the pool, which keeps the native file handle alive.  Without
        // ClearAllPools() the subsequent Directory.Move would fail with "Access denied".
        var writer = new DataPackWriter(_root);
        writer.Write(CreateMetadata(), _ => { });
        new DataPackReader(_root).OpenDatabase().Dispose(); // disposed → goes to pool

        // Write() calls ClearAllPools() before Promote(), releasing pooled handles so
        // Directory.Move(data → data.previous-*) succeeds on Windows with Defender active.
        writer.Write(CreateMetadata(), _ => { });

        // The new pack is the active one and is immediately readable.
        using SqliteConnection v2Connection = new DataPackReader(_root).OpenDatabase();
        Assert.NotNull(v2Connection);
    }

    [Fact]
    public void UserPathsHonorsDataDirectoryOverride()
    {
        string overrideDirectory = Path.Combine(_root, "configured-data");
        string? previous = Environment.GetEnvironmentVariable("DW3_DATA_DIR");
        try
        {
            Environment.SetEnvironmentVariable("DW3_DATA_DIR", overrideDirectory);
            Assert.Equal(Path.GetFullPath(overrideDirectory), UserPaths.GetDataDirectory());
        }
        finally
        {
            Environment.SetEnvironmentVariable("DW3_DATA_DIR", previous);
        }
    }

    [Fact]
    public void ReaderOpensDatabaseReadOnly()
    {
        var writer = new DataPackWriter(_root);
        writer.Write(CreateMetadata(), _ => { });

        using SqliteConnection connection = new DataPackReader(_root).OpenDatabase();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE rejected_write (id INTEGER)";

        Assert.Throws<SqliteException>(() => command.ExecuteNonQuery());
    }

    public void Dispose()
    {
        // Release pooled connections before deletion; on Windows, the pool keeps
        // the file handle open past Dispose(), preventing Directory.Delete.
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static DataPackMetadata CreateMetadata() => new(
        ExtractorVersion: "test-extractor",
        DiscSha1: "0123456789012345678901234567890123456789",
        Edition: "test-edition",
        CompletedAt: DateTimeOffset.Parse("2026-01-01T00:00:00+00:00", System.Globalization.CultureInfo.InvariantCulture));
}
