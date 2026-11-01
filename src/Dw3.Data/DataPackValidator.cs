using Microsoft.Data.Sqlite;

namespace Dw3.Data;

internal static class DataPackValidator
{
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
