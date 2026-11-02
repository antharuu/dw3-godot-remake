using Microsoft.Data.Sqlite;

namespace Dw3.Data;

/// <summary>
/// Validates an extracted data pack by checking the directory layout, database integrity,
/// schema version, required metadata fields, and the presence of every registered image file.
/// Called by both <see cref="DataPackWriter"/> after staging and <see cref="DataPackReader"/> before opening.
/// </summary>
internal static class DataPackValidator
{
    /// <summary>
    /// Validates the data pack at <paramref name="dataDirectory"/>, raising a typed
    /// <see cref="DataPackException"/> when any structural or integrity check fails.
    /// </summary>
    /// <param name="dataDirectory">Absolute path of the directory containing <c>dw3.sqlite</c>.</param>
    /// <param name="expectedSchemaVersion">Schema version this runtime requires; checked against the <c>meta</c> row.</param>
    /// <exception cref="DataPackMissing">Thrown when <paramref name="dataDirectory"/> does not exist.</exception>
    /// <exception cref="DataPackIncomplete">Thrown when the database file is absent or the <c>meta</c> table is missing or incomplete.</exception>
    /// <exception cref="DataPackSchemaMismatch">Thrown when the pack schema version differs from <paramref name="expectedSchemaVersion"/>.</exception>
    /// <exception cref="DataPackCorrupt">Thrown when the SQLite integrity check fails, the metadata count is wrong, or an image file is missing.</exception>
    internal static void Validate(string dataDirectory, int expectedSchemaVersion)
    {
        if (!Directory.Exists(dataDirectory))
        {
            throw new DataPackMissing("No extracted data pack is available.");
        }

        string databasePath = Path.Combine(dataDirectory, "dw3.sqlite");
        if (!File.Exists(databasePath))
        {
            throw new DataPackIncomplete("The extracted data pack has no database.");
        }

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

    /// <summary>
    /// Runs the SQLite <c>PRAGMA quick_check</c> and raises <see cref="DataPackCorrupt"/>
    /// if the database reports any structural problem.
    /// </summary>
    /// <param name="connection">Open read-only connection to the pack database.</param>
    /// <exception cref="DataPackCorrupt">Thrown when the integrity check returns a result other than <c>ok</c>.</exception>
    private static void VerifyDatabaseIntegrity(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA quick_check";
        string result = command.ExecuteScalar() as string ?? string.Empty;
        if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
        {
            throw new DataPackCorrupt("The data-pack database integrity check failed.");
        }
    }

    /// <summary>
    /// Reads the single row from the <c>meta</c> table and validates the schema version
    /// and the non-empty presence of all required text fields.
    /// </summary>
    /// <param name="connection">Open read-only connection to the pack database.</param>
    /// <param name="expectedSchemaVersion">Schema version this runtime requires.</param>
    /// <exception cref="DataPackIncomplete">Thrown when the <c>meta</c> table is absent, has no rows, or contains blank fields.</exception>
    /// <exception cref="DataPackSchemaMismatch">Thrown when the stored schema version differs from <paramref name="expectedSchemaVersion"/>.</exception>
    /// <exception cref="DataPackCorrupt">Thrown when the <c>meta</c> table contains more than one row.</exception>
    private static void VerifyMetadata(SqliteConnection connection, int expectedSchemaVersion)
    {
        try
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT schema_version, extractor_version, disc_sha1, edition, completed_at FROM meta";
            using SqliteDataReader reader = command.ExecuteReader();
            if (!reader.Read())
            {
                throw new DataPackIncomplete("The extracted data pack has no completion metadata.");
            }

            int schemaVersion = reader.GetInt32(0);
            if (schemaVersion != expectedSchemaVersion)
            {
                throw new DataPackSchemaMismatch($"The data pack uses schema version {schemaVersion}, but this runtime requires version {expectedSchemaVersion}.");
            }

            for (int column = 1; column < reader.FieldCount; column++)
            {
                if (reader.IsDBNull(column) || string.IsNullOrWhiteSpace(reader.GetString(column)))
                {
                    throw new DataPackIncomplete("The extracted data pack has incomplete metadata.");
                }
            }

            if (reader.Read())
            {
                throw new DataPackCorrupt("The data pack contains more than one metadata record.");
            }
        }
        catch (SqliteException exception)
        {
            throw new DataPackIncomplete("The extracted data pack is missing its metadata table.", exception);
        }
    }

    /// <summary>
    /// Reads every path registered in the <c>pack_images</c> table and confirms that
    /// the corresponding file exists on disk relative to <paramref name="dataDirectory"/>.
    /// </summary>
    /// <param name="connection">Open read-only connection to the pack database.</param>
    /// <param name="dataDirectory">Absolute path of the directory containing the extracted image files.</param>
    /// <exception cref="DataPackIncomplete">Thrown when the <c>pack_images</c> table is absent.</exception>
    /// <exception cref="DataPackCorrupt">Thrown when a registered image path is invalid or the corresponding file is missing.</exception>
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
                {
                    throw new DataPackCorrupt($"The data pack references a missing image: {normalizedPath}.");
                }
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
