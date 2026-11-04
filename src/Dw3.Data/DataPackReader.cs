using Microsoft.Data.Sqlite;

namespace Dw3.Data;

/// <summary>
/// Opens and validates the active extracted data pack without granting write access.
/// </summary>
public sealed class DataPackReader
{
    private const string DataDirectoryName = "data";
    private readonly string _rootDirectory;

    /// <summary>
    /// Initializes a new instance of the <see cref="DataPackReader"/> class for the active data
    /// directory below the supplied root, resolving the path to an absolute form.
    /// </summary>
    /// <param name="rootDirectory">Parent directory containing the active <c>data</c> sub-directory.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="rootDirectory"/> is null, empty, or whitespace.</exception>
    public DataPackReader(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        _rootDirectory = Path.GetFullPath(rootDirectory);
    }

    /// <summary>Gets the validated pack's active data directory.</summary>
    public string DataDirectory => Path.Combine(_rootDirectory, DataDirectoryName);

    /// <summary>
    /// Resolves a pack-relative path without allowing it to escape the active data directory.
    /// The caller decides whether absence of the resolved file signals an incomplete or corrupt pack.
    /// </summary>
    /// <param name="relativePath">
    /// Slash-separated path beneath the data directory.
    /// Must not be empty, rooted, or contain <c>..</c> or empty segments.
    /// </param>
    /// <returns>The absolute, normalized path on disk, guaranteed to lie inside the active data directory.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="relativePath"/> is null, empty, whitespace, or would escape the data directory.</exception>
    public string ResolvePath(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        string dataDirectory = DataDirectory;
        string resolved = Path.GetFullPath(Path.Combine(dataDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        string prefix = dataDirectory.EndsWith(Path.DirectorySeparatorChar)
            ? dataDirectory
            : dataDirectory + Path.DirectorySeparatorChar;
        if (!resolved.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new ArgumentException("A data-pack path cannot escape the active data directory.", nameof(relativePath));
        }

        return resolved;
    }

    /// <summary>
    /// Validates the active pack and returns an open SQLite connection in read-only mode.
    /// </summary>
    /// <returns>An open read-only connection. The caller owns and must dispose it.</returns>
    /// <exception cref="DataPackMissingException">Thrown when no active pack exists.</exception>
    /// <exception cref="DataPackIncompleteException">Thrown when required pack metadata is absent.</exception>
    /// <exception cref="DataPackSchemaMismatchException">Thrown when the pack schema is unsupported.</exception>
    /// <exception cref="DataPackCorruptException">Thrown when database or image integrity validation fails.</exception>
    public SqliteConnection OpenDatabase()
    {
        string dataDirectory = Path.Combine(_rootDirectory, DataDirectoryName);
        DataPackValidator.Validate(dataDirectory, DataPackSchema.Version);

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(dataDirectory, "dw3.sqlite"),
            Mode = SqliteOpenMode.ReadOnly,

            // Not pooled, so disposing the connection releases dw3.sqlite: on Windows a pooled handle
            // blocks the replacement of the pack by a later extraction.
            Pooling = false,
        };
        try
        {
            var connection = new SqliteConnection(builder.ConnectionString);
            connection.Open();
            return connection;
        }
        catch (SqliteException exception)
        {
            throw new DataPackCorruptException("The data-pack database could not be opened read-only.", exception);
        }
    }
}
