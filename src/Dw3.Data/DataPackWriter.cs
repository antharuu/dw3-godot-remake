using Microsoft.Data.Sqlite;

namespace Dw3.Data;

/// <summary>
/// Builds one data pack in an isolated staging directory before promoting it to the active location.
/// </summary>
public sealed class DataPackWriter
{
    private const string DataDirectoryName = "data";
    private readonly string _rootDirectory;

    /// <summary>
    /// Initializes a writer and removes stale staging directories from interrupted earlier extractions.
    /// </summary>
    /// <param name="rootDirectory">Parent directory that will contain the active <c>data</c> directory.</param>
    public DataPackWriter(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        _rootDirectory = Path.GetFullPath(rootDirectory);
        Directory.CreateDirectory(_rootDirectory);
        RemoveOrphanedStagingDirectories();
    }

    /// <summary>
    /// Writes, validates, and atomically promotes a complete data pack.
    /// </summary>
    /// <param name="metadata">Source identity and extractor completion metadata.</param>
    /// <param name="writeContents">Writes pack-specific tables and image files into the staging directory.</param>
    public void Write(DataPackMetadata metadata, Action<DataPackWriteContext> writeContents)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(writeContents);
        ValidateMetadata(metadata);

        string stagingDirectory = Path.Combine(_rootDirectory, $"data.staging-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(stagingDirectory);
            string databasePath = Path.Combine(stagingDirectory, "dw3.sqlite");
            using (var connection = new SqliteConnection($"Data Source={databasePath}"))
            {
                connection.Open();
                using SqliteTransaction transaction = connection.BeginTransaction();
                DataPackSchema.Create(connection);
                var context = new DataPackWriteContext(connection, transaction, stagingDirectory);
                writeContents(context);
                InsertMetadata(connection, transaction, metadata);
                transaction.Commit();
            }

            DataPackValidator.Validate(stagingDirectory, DataPackSchema.Version);
            Promote(stagingDirectory);
        }
        catch
        {
            if (Directory.Exists(stagingDirectory))
                Directory.Delete(stagingDirectory, recursive: true);
            throw;
        }
    }

    private void Promote(string stagingDirectory)
    {
        string activeDirectory = Path.Combine(_rootDirectory, DataDirectoryName);
        string? previousDirectory = null;
        try
        {
            if (Directory.Exists(activeDirectory))
            {
                previousDirectory = Path.Combine(_rootDirectory, $"data.previous-{Guid.NewGuid():N}");
                Directory.Move(activeDirectory, previousDirectory);
            }

            Directory.Move(stagingDirectory, activeDirectory);
        }
        catch
        {
            if (previousDirectory is not null && Directory.Exists(previousDirectory) && !Directory.Exists(activeDirectory))
                Directory.Move(previousDirectory, activeDirectory);
            throw;
        }

        if (previousDirectory is not null && Directory.Exists(previousDirectory))
            Directory.Delete(previousDirectory, recursive: true);
    }

    private void RemoveOrphanedStagingDirectories()
    {
        foreach (string directory in Directory.EnumerateDirectories(_rootDirectory, "data.staging-*", SearchOption.TopDirectoryOnly))
            Directory.Delete(directory, recursive: true);
    }

    private static void InsertMetadata(SqliteConnection connection, SqliteTransaction transaction, DataPackMetadata metadata)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO meta (schema_version, extractor_version, disc_sha1, edition, completed_at)
            VALUES ($schemaVersion, $extractorVersion, $discSha1, $edition, $completedAt)
            """;
        command.Parameters.AddWithValue("$schemaVersion", DataPackSchema.Version);
        command.Parameters.AddWithValue("$extractorVersion", metadata.ExtractorVersion);
        command.Parameters.AddWithValue("$discSha1", metadata.DiscSha1);
        command.Parameters.AddWithValue("$edition", metadata.Edition);
        command.Parameters.AddWithValue("$completedAt", metadata.CompletedAt.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }

    private static void ValidateMetadata(DataPackMetadata metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata.ExtractorVersion) || string.IsNullOrWhiteSpace(metadata.DiscSha1) || string.IsNullOrWhiteSpace(metadata.Edition))
            throw new ArgumentException("Data-pack metadata fields must be non-empty.", nameof(metadata));
    }
}

/// <summary>
/// Supplies a transaction and staging paths to extractor code writing one data pack.
/// </summary>
public sealed class DataPackWriteContext
{
    private readonly string _stagingDirectory;

    internal DataPackWriteContext(SqliteConnection database, SqliteTransaction transaction, string stagingDirectory)
    {
        Database = database;
        Transaction = transaction;
        _stagingDirectory = stagingDirectory;
    }

    /// <summary>
    /// Gets the open database connection for creating domain tables and inserting extracted data.
    /// </summary>
    public SqliteConnection Database { get; }

    /// <summary>
    /// Gets the transaction that must be assigned to commands written through <see cref="Database"/>.
    /// </summary>
    public SqliteTransaction Transaction { get; }

    /// <summary>
    /// Resolves a relative image path inside the staging directory.
    /// </summary>
    /// <param name="relativePath">Slash-separated image path beneath the pack root.</param>
    /// <returns>Absolute path inside the current staging directory.</returns>
    public string GetImagePath(string relativePath) => ResolveRelativePath(relativePath);

    /// <summary>
    /// Registers an image that must exist before the pack can be promoted.
    /// </summary>
    /// <param name="relativePath">Slash-separated image path beneath the pack root.</param>
    public void RegisterImage(string relativePath)
    {
        string normalizedPath = NormalizeRelativePath(relativePath);
        using SqliteCommand command = Database.CreateCommand();
        command.Transaction = Transaction;
        command.CommandText = "INSERT INTO pack_images (relative_path) VALUES ($relativePath)";
        command.Parameters.AddWithValue("$relativePath", normalizedPath);
        command.ExecuteNonQuery();
    }

    private string ResolveRelativePath(string relativePath)
    {
        string normalizedPath = NormalizeRelativePath(relativePath);
        return Path.Combine(_stagingDirectory, normalizedPath.Replace('/', Path.DirectorySeparatorChar));
    }

    internal static string NormalizeRelativePath(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        string normalizedPath = relativePath.Replace('\\', '/');
        if (Path.IsPathRooted(normalizedPath) || normalizedPath.Split('/').Any(part => part is "." or ".." || part.Length == 0))
            throw new ArgumentException("Image paths must be non-rooted paths beneath the data pack.", nameof(relativePath));
        return normalizedPath;
    }
}
