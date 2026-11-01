using Microsoft.Data.Sqlite;

namespace Dw3.Data;

/// <summary>
/// Opens and validates the active extracted data pack without granting write access.
/// </summary>
public sealed class DataPackReader
{
    private const string DataDirectoryName = "data";
    private readonly string _rootDirectory;

    /// <summary>Gets the validated pack's active data directory.</summary>
    public string DataDirectory => Path.Combine(_rootDirectory, DataDirectoryName);

    /// <summary>
    /// Resolves a pack-relative path without allowing it to escape the active data directory.
    /// The caller still decides whether absence is an incomplete or corrupt domain contract.
    /// </summary>
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
    /// Initializes a reader for the active data directory below the supplied root.
    /// </summary>
    /// <param name="rootDirectory">Parent directory containing the active <c>data</c> directory.</param>
    public DataPackReader(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        _rootDirectory = Path.GetFullPath(rootDirectory);
    }

    /// <summary>
    /// Validates the active pack and returns an open SQLite connection in read-only mode.
    /// </summary>
    /// <returns>An open read-only connection. The caller owns and must dispose it.</returns>
    /// <exception cref="DataPackMissing">Thrown when no active pack exists.</exception>
    /// <exception cref="DataPackIncomplete">Thrown when required pack metadata is absent.</exception>
    /// <exception cref="DataPackSchemaMismatch">Thrown when the pack schema is unsupported.</exception>
    /// <exception cref="DataPackCorrupt">Thrown when database or image integrity validation fails.</exception>
    public SqliteConnection OpenDatabase()
    {
        string dataDirectory = Path.Combine(_rootDirectory, DataDirectoryName);
        DataPackValidator.Validate(dataDirectory, DataPackSchema.Version);

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(dataDirectory, "dw3.sqlite"),
            Mode = SqliteOpenMode.ReadOnly,
        };
        try
        {
            var connection = new SqliteConnection(builder.ConnectionString);
            connection.Open();
            return connection;
        }
        catch (SqliteException exception)
        {
            throw new DataPackCorrupt("The data-pack database could not be opened read-only.", exception);
        }
    }
}
