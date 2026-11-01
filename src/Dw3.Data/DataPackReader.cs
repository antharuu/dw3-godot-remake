using Microsoft.Data.Sqlite;

namespace Dw3.Data;

/// <summary>
/// Opens and validates the active extracted data pack without granting write access.
/// </summary>
public sealed class DataPackReader
{
    private const string DataDirectoryName = "data";
    private readonly string _rootDirectory;

    /// <summary>The validated pack's active data directory.</summary>
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
            throw new ArgumentException("A data-pack path cannot escape the active data directory.", nameof(relativePath));
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

internal static class DataPackValidator
{
    internal static void Validate(string dataDirectory, int expectedSchemaVersion)
    {
        if (!Directory.Exists(dataDirectory))
            throw new DataPackMissing("No extracted data pack is available.");

        string databasePath = Path.Combine(dataDirectory, "dw3.sqlite");
        if (!File.Exists(databasePath))
            throw new DataPackIncomplete("The extracted data pack has no database.");

        try
        {
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadOnly,
            };
            using var connection = new SqliteConnection(builder.ConnectionString);
            connection.Open();
            VerifyDatabaseIntegrity(connection);
            VerifyMetadata(connection, expectedSchemaVersion);
            VerifyImages(connection, dataDirectory);
        }
        catch (DataPackException)
        {
            throw;
        }
        catch (SqliteException exception)
        {
            throw new DataPackCorrupt("The data-pack database is corrupt.", exception);
        }
    }

    private static void VerifyDatabaseIntegrity(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA quick_check";
        string result = command.ExecuteScalar() as string ?? string.Empty;
        if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
            throw new DataPackCorrupt("The data-pack database integrity check failed.");
    }

    private static void VerifyMetadata(SqliteConnection connection, int expectedSchemaVersion)
    {
        try
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT schema_version, extractor_version, disc_sha1, edition, completed_at FROM meta";
            using SqliteDataReader reader = command.ExecuteReader();
            if (!reader.Read())
                throw new DataPackIncomplete("The extracted data pack has no completion metadata.");

            int schemaVersion = reader.GetInt32(0);
            if (schemaVersion != expectedSchemaVersion)
                throw new DataPackSchemaMismatch($"The data pack uses schema version {schemaVersion}, but this runtime requires version {expectedSchemaVersion}.");

            for (int column = 1; column < reader.FieldCount; column++)
            {
                if (reader.IsDBNull(column) || string.IsNullOrWhiteSpace(reader.GetString(column)))
                    throw new DataPackIncomplete("The extracted data pack has incomplete metadata.");
            }

            if (reader.Read())
                throw new DataPackCorrupt("The data pack contains more than one metadata record.");
        }
        catch (SqliteException exception)
        {
            throw new DataPackIncomplete("The extracted data pack is missing its metadata table.", exception);
        }
    }

    private static void VerifyImages(SqliteConnection connection, string dataDirectory)
    {
        try
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT relative_path FROM pack_images";
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                string relativePath = reader.GetString(0);
                string normalizedPath;
                try
                {
                    normalizedPath = DataPackWriteContext.NormalizeRelativePath(relativePath);
                }
                catch (ArgumentException exception)
                {
                    throw new DataPackCorrupt("The data pack contains an invalid image path.", exception);
                }

                string imagePath = Path.Combine(dataDirectory, normalizedPath.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(imagePath))
                    throw new DataPackCorrupt($"The data pack references a missing image: {normalizedPath}.");
            }
        }
        catch (DataPackCorrupt)
        {
            throw;
        }
        catch (SqliteException exception)
        {
            throw new DataPackIncomplete("The extracted data pack is missing its image manifest.", exception);
        }
    }
}
