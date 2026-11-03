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
        Assert.Throws<DataPackMissingException>(() => new DataPackReader(_root).OpenDatabase());
    }

    [Fact]
    public void ReaderRejectsPackWithoutDatabase()
    {
        Directory.CreateDirectory(Path.Combine(_root, "data"));

        var exception = Assert.Throws<DataPackIncompleteException>(() => new DataPackReader(_root).OpenDatabase());

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

        Assert.Throws<DataPackSchemaMismatchException>(() => new DataPackReader(_root).OpenDatabase());
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

        Assert.Throws<DataPackCorruptException>(() => new DataPackReader(_root).OpenDatabase());
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

    [Fact]
    public void ReaderRejectsPackWithoutMetadataRecord()
    {
        CreateRawPack(DataPackSchema.Create);

        var exception = Assert.Throws<DataPackIncompleteException>(() => new DataPackReader(_root).OpenDatabase());

        Assert.Contains("completion metadata", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReaderRejectsPackWithBlankMetadataField()
    {
        CreateRawPack(connection =>
        {
            DataPackSchema.Create(connection);
            InsertMeta(connection, DataPackSchema.Version, edition: " ");
        });

        var exception = Assert.Throws<DataPackIncompleteException>(() => new DataPackReader(_root).OpenDatabase());

        Assert.Contains("incomplete metadata", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReaderRejectsPackWithSeveralMetadataRecords()
    {
        CreateRawPack(connection =>
        {
            DataPackSchema.Create(connection);
            InsertMeta(connection, DataPackSchema.Version, edition: "first");
            InsertMeta(connection, DataPackSchema.Version, edition: "second");
        });

        var exception = Assert.Throws<DataPackCorruptException>(() => new DataPackReader(_root).OpenDatabase());

        Assert.Contains("more than one metadata record", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReaderRejectsPackWithoutMetadataTable()
    {
        CreateRawPack(connection => Execute(connection, "CREATE TABLE unrelated (id INTEGER)"));

        var exception = Assert.Throws<DataPackIncompleteException>(() => new DataPackReader(_root).OpenDatabase());

        Assert.Contains("metadata table", exception.Message, StringComparison.Ordinal);
        Assert.IsType<SqliteException>(exception.InnerException);
    }

    [Fact]
    public void ReaderRejectsPackWithoutImageManifest()
    {
        CreateRawPack(connection =>
        {
            Execute(connection, "CREATE TABLE meta (schema_version INTEGER, extractor_version TEXT, disc_sha1 TEXT, edition TEXT, completed_at TEXT)");
            InsertMeta(connection, DataPackSchema.Version, edition: "test-edition");
        });

        var exception = Assert.Throws<DataPackIncompleteException>(() => new DataPackReader(_root).OpenDatabase());

        Assert.Contains("image manifest", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("../escape.png")]
    [InlineData("images//double.png")]
    [InlineData("/rooted.png")]
    public void ReaderRejectsImagePathOutsideThePack(string relativePath)
    {
        CreateRawPack(connection =>
        {
            DataPackSchema.Create(connection);
            InsertMeta(connection, DataPackSchema.Version, edition: "test-edition");
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "INSERT INTO pack_images (relative_path) VALUES ($path)";
            command.Parameters.AddWithValue("$path", relativePath);
            command.ExecuteNonQuery();
        });

        var exception = Assert.Throws<DataPackCorruptException>(() => new DataPackReader(_root).OpenDatabase());

        Assert.IsType<ArgumentException>(exception.InnerException);
    }

    [Fact]
    public void ReaderReportsUnreadableDatabaseAsCorrupt()
    {
        string data = Path.Combine(_root, "data");
        Directory.CreateDirectory(data);
        File.WriteAllBytes(Path.Combine(data, "dw3.sqlite"), [.. Enumerable.Repeat((byte)0x5A, 4096)]);

        var exception = Assert.Throws<DataPackCorruptException>(() => new DataPackReader(_root).OpenDatabase());

        Assert.IsType<SqliteException>(exception.InnerException);
    }

    [Fact]
    public void ReaderAcceptsPackWithRegisteredImage()
    {
        var writer = new DataPackWriter(_root);
        writer.Write(CreateMetadata(), pack =>
        {
            string imagePath = pack.GetImagePath("images\\field\\2.png");
            Directory.CreateDirectory(Path.GetDirectoryName(imagePath)!);
            File.WriteAllBytes(imagePath, [1, 2, 3]);
            pack.RegisterImage("images\\field\\2.png");
        });

        using SqliteConnection connection = new DataPackReader(_root).OpenDatabase();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT relative_path FROM pack_images";

        Assert.Equal("images/field/2.png", command.ExecuteScalar());
    }

    [Theory]
    [InlineData("", "sha", "edition")]
    [InlineData("extractor", " ", "edition")]
    [InlineData("extractor", "sha", "")]
    public void WriterRejectsBlankMetadataBeforeStaging(string extractorVersion, string discSha1, string edition)
    {
        var writer = new DataPackWriter(_root);
        var metadata = new DataPackMetadata(extractorVersion, discSha1, edition, DateTimeOffset.UnixEpoch);

        Assert.Throws<ArgumentException>(() => writer.Write(metadata, _ => { }));

        Assert.Empty(Directory.EnumerateDirectories(_root));
    }

    [Fact]
    public void WriterRejectsNullArguments()
    {
        var writer = new DataPackWriter(_root);

        Assert.Throws<ArgumentNullException>(() => writer.Write(null!, _ => { }));
        Assert.Throws<ArgumentNullException>(() => writer.Write(CreateMetadata(), null!));
        Assert.Throws<ArgumentException>(() => new DataPackWriter(" "));
        Assert.Throws<ArgumentException>(() => new DataPackReader(string.Empty));
    }

    [Fact]
    public void WriterRemovesOrphanedPreviousPackDirectoriesOnStartup()
    {
        string previous = Path.Combine(_root, "data.previous-orphan");
        Directory.CreateDirectory(previous);
        File.WriteAllText(Path.Combine(previous, "dw3.sqlite"), "stale");

        _ = new DataPackWriter(_root);

        Assert.False(Directory.Exists(previous));
    }

    [Fact]
    public void WriterReplacesActivePackAndDropsThePreviousOne()
    {
        var writer = new DataPackWriter(_root);
        writer.Write(CreateMetadata(), _ => { });
        writer.Write(CreateMetadata() with { Edition = "second-edition" }, _ => { });

        using SqliteConnection connection = new DataPackReader(_root).OpenDatabase();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT edition FROM meta";

        Assert.Equal("second-edition", command.ExecuteScalar());
        Assert.Empty(Directory.EnumerateDirectories(_root, "data.previous-*"));
    }

    [Fact]
    public void ResolvePathStaysInsideTheActiveDataDirectory()
    {
        var reader = new DataPackReader(_root);

        Assert.Equal(
            Path.Combine(reader.DataDirectory, "images", "a.png"),
            reader.ResolvePath("images/a.png"));
        Assert.Throws<ArgumentException>(() => reader.ResolvePath("../outside.png"));
        Assert.Throws<ArgumentException>(() => reader.ResolvePath(" "));
    }

    [Fact]
    public void UserPathsFollowsXdgDataHomeOnLinux()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string xdg = Path.Combine(_root, "xdg");
        string? previousOverride = Environment.GetEnvironmentVariable("DW3_DATA_DIR");
        string? previousXdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        try
        {
            Environment.SetEnvironmentVariable("DW3_DATA_DIR", null);
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", xdg);
            Assert.Equal(Path.Combine(xdg, "dw3-remake"), UserPaths.GetDataDirectory());

            Environment.SetEnvironmentVariable("XDG_DATA_HOME", null);
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            Assert.Equal(Path.GetFullPath(Path.Combine(home, ".local", "share", "dw3-remake")), UserPaths.GetDataDirectory());
        }
        finally
        {
            Environment.SetEnvironmentVariable("DW3_DATA_DIR", previousOverride);
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", previousXdg);
        }
    }

    public void Dispose()
    {
        // Release pooled connections before deletion; on Windows, the pool keeps
        // the file handle open past Dispose(), preventing Directory.Delete.
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static void InsertMeta(SqliteConnection connection, int schemaVersion, string edition)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "INSERT INTO meta (schema_version, extractor_version, disc_sha1, edition, completed_at) VALUES ($version, 'test', '0123456789012345678901234567890123456789', $edition, '2026-01-01T00:00:00.0000000+00:00')";
        command.Parameters.AddWithValue("$version", schemaVersion);
        command.Parameters.AddWithValue("$edition", edition);
        command.ExecuteNonQuery();
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static DataPackMetadata CreateMetadata() => new(
        ExtractorVersion: "test-extractor",
        DiscSha1: "0123456789012345678901234567890123456789",
        Edition: "test-edition",
        CompletedAt: DateTimeOffset.Parse("2026-01-01T00:00:00+00:00", System.Globalization.CultureInfo.InvariantCulture));

    private void CreateRawPack(Action<SqliteConnection> build)
    {
        string data = Path.Combine(_root, "data");
        Directory.CreateDirectory(data);
        using var connection = new SqliteConnection($"Data Source={Path.Combine(data, "dw3.sqlite")};Pooling=False");
        connection.Open();
        build(connection);
    }
}
