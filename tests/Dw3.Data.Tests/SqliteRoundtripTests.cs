using Microsoft.Data.Sqlite;
using Xunit;

namespace Dw3.Data.Tests;

/// <summary>
/// Verifies that Microsoft.Data.Sqlite is functional in this project: create a
/// temporary database, write one row, and read it back.  This test exercises the
/// full native SQLite stack (e_sqlite3) without touching any disc data.
/// </summary>
public sealed class SqliteRoundtripTests : IDisposable
{
    private readonly string _dbPath;

    public SqliteRoundtripTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"dw3_sqlite_probe_{Guid.NewGuid():N}.db");
    }

    [Fact]
    public void WriteAndReadRowRoundtrips()
    {
        const string expectedValue = "sqlite-ok";

        // --- write ---
        using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            conn.Open();
            using var create = conn.CreateCommand();
            create.CommandText = "CREATE TABLE probe (key TEXT PRIMARY KEY, value TEXT NOT NULL)";
            create.ExecuteNonQuery();

            using var insert = conn.CreateCommand();
            insert.CommandText = "INSERT INTO probe (key, value) VALUES ('test', @v)";
            insert.Parameters.AddWithValue("@v", expectedValue);
            insert.ExecuteNonQuery();
        }

        // --- read back in a fresh connection to rule out in-memory caching ---
        string actualValue;
        using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            conn.Open();
            using var select = conn.CreateCommand();
            select.CommandText = "SELECT value FROM probe WHERE key = 'test'";
            actualValue = (string)(select.ExecuteScalar() ?? throw new InvalidOperationException("no row returned"));
        }

        Assert.Equal(expectedValue, actualValue);
    }

    public void Dispose()
    {
        // Release pooled connections before deletion; on Windows, the pool keeps
        // the file handle open past Dispose(), preventing File.Delete.
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }
}
