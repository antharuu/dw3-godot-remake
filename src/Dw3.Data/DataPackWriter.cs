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
    /// Initializes a new instance of the <see cref="DataPackWriter"/> class, creating the root directory
    /// and removing stale staging or previous-pack directories left by interrupted earlier extractions.
    /// </summary>
    /// <param name="rootDirectory">Parent directory that will contain the active <c>data</c> sub-directory.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="rootDirectory"/> is null, empty, or whitespace.</exception>
    public DataPackWriter(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        _rootDirectory = Path.GetFullPath(rootDirectory);
        Directory.CreateDirectory(_rootDirectory);
        RemoveOrphanedDirectories();
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
            // Pooling=False ensures the write connection truly closes its native file handle on
            // Dispose() rather than returning it to the connection pool.  Without this flag the
            // pool can keep the handle open past Dispose(), preventing Directory.Move on Windows.
            using (var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False"))
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
            // Release any pooled SQLite connections that may hold a native file handle
            // on the staging database before renaming the directory. On Windows, an open
            // handle prevents Directory.Move even after the SqliteConnection is disposed,
            // because the connection pool keeps the underlying handle alive.
            SqliteConnection.ClearAllPools();
            Promote(stagingDirectory);
        }
        catch
        {
            // On Windows, Microsoft.Data.Sqlite connection pooling may keep the file handle
            // open even after Dispose(), preventing Directory.Delete from succeeding.
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(stagingDirectory))
            {
                Directory.Delete(stagingDirectory, recursive: true);
            }

            throw;
        }
    }

    /// <summary>
    /// Atomically replaces the active data directory with <paramref name="stagingDirectory"/>,
    /// preserving a backup copy of the previous active directory until the rename succeeds.
    /// The backup is removed on success; orphaned backups are cleaned up on the next construction.
    /// </summary>
    /// <param name="stagingDirectory">Validated staging directory to promote to the active location.</param>
    private void Promote(string stagingDirectory)
    {
        string activeDirectory = Path.Combine(_rootDirectory, DataDirectoryName);
        string? previousDirectory = null;
        try
        {
            if (Directory.Exists(activeDirectory))
            {
                previousDirectory = Path.Combine(_rootDirectory, $"data.previous-{Guid.NewGuid():N}");
                // Use the same retry logic as staging→active: on Windows, Defender or an
                // antivirus may briefly hold a handle on a file inside the active directory
                // even after ClearAllPools().  An exponential back-off lets the handle
                // release before the rename is attempted again.
                MoveWithRetry(activeDirectory, previousDirectory);
            }

            MoveWithRetry(stagingDirectory, activeDirectory);
        }
        catch
        {
            if (previousDirectory is not null && Directory.Exists(previousDirectory) && !Directory.Exists(activeDirectory))
            {
                Directory.Move(previousDirectory, activeDirectory);
            }

            throw;
        }

        if (previousDirectory is not null && Directory.Exists(previousDirectory))
        {
            // Best-effort: if an open reader holds a native handle on a file inside the previous
            // directory, Directory.Delete throws IOException or UnauthorizedAccessException on
            // Windows.  The pack has already been promoted successfully at this point, so a
            // cleanup failure is non-critical.  Orphaned data.previous-* directories are removed
            // on the next DataPackWriter construction via RemoveOrphanedDirectories().
            TryDeleteDirectory(previousDirectory);
        }
    }

    /// <summary>
    /// Moves a directory, retrying on transient <see cref="IOException"/> or
    /// <see cref="UnauthorizedAccessException"/> failures.
    /// On Windows, antivirus or file-system indexers may briefly hold a handle on a
    /// file inside the source directory, causing <c>Directory.Move</c> to throw.
    /// An exponential back-off gives the external process time to release the handle.
    /// </summary>
    /// <param name="source">Absolute path of the directory to move.</param>
    /// <param name="destination">Absolute path the directory should be moved to; must not exist.</param>
    private static void MoveWithRetry(string source, string destination)
    {
        const int MaxAttempts = 8;
        const int InitialDelayMs = 50;
        for (int attempt = 0; attempt < MaxAttempts; attempt++)
        {
            try
            {
                Directory.Move(source, destination);
                return;
            }
            catch (IOException) when (attempt < MaxAttempts - 1)
            {
                System.Threading.Thread.Sleep(InitialDelayMs << attempt);
            }
            catch (UnauthorizedAccessException) when (attempt < MaxAttempts - 1)
            {
                System.Threading.Thread.Sleep(InitialDelayMs << attempt);
            }
        }
    }

    /// <summary>
    /// Deletes orphaned staging (<c>data.staging-*</c>) and superseded previous-pack
    /// (<c>data.previous-*</c>) directories below the root.
    /// Failures are silently swallowed because cleanup is best-effort and will be retried
    /// on the next <see cref="DataPackWriter"/> construction.
    /// </summary>
    private void RemoveOrphanedDirectories()
    {
        // Remove incomplete staging directories left by interrupted extractions.
        foreach (string directory in Directory.EnumerateDirectories(_rootDirectory, "data.staging-*", SearchOption.TopDirectoryOnly))
        {
            TryDeleteDirectory(directory);
        }

        // Remove superseded previous-pack directories whose cleanup was deferred because
        // an open reader held a file handle at promotion time (Windows only).
        foreach (string directory in Directory.EnumerateDirectories(_rootDirectory, "data.previous-*", SearchOption.TopDirectoryOnly))
        {
            TryDeleteDirectory(directory);
        }
    }

    /// <summary>
    /// Attempts to recursively delete a directory, silently swallowing transient failures.
    /// A failed deletion is not a correctness issue: the directory will be retried on the
    /// next <see cref="DataPackWriter"/> construction via <see cref="RemoveOrphanedDirectories"/>.
    /// </summary>
    /// <param name="directory">Absolute path of the directory to delete recursively.</param>
    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>
    /// Writes the single <c>meta</c> row into the pack database, binding extractor version,
    /// disc SHA-1, edition tag, and UTC completion timestamp alongside the current schema version.
    /// </summary>
    /// <param name="connection">Open SQLite connection to the staging database.</param>
    /// <param name="transaction">Active transaction that must wrap the insert.</param>
    /// <param name="metadata">Validated source-identity and completion metadata to persist.</param>
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

    /// <summary>
    /// Confirms that the extractor version, disc SHA-1, and edition fields are all non-empty.
    /// Called before staging begins so that an obvious metadata defect is caught immediately.
    /// </summary>
    /// <param name="metadata">Metadata to inspect before writing the staging database.</param>
    /// <exception cref="ArgumentException">Thrown when any required metadata field is null or whitespace.</exception>
    private static void ValidateMetadata(DataPackMetadata metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata.ExtractorVersion) || string.IsNullOrWhiteSpace(metadata.DiscSha1) || string.IsNullOrWhiteSpace(metadata.Edition))
        {
            throw new ArgumentException("Data-pack metadata fields must be non-empty.", nameof(metadata));
        }
    }
}
