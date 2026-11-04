using Dw3Production.Logging;
using Godot;
using Microsoft.Data.Sqlite;

namespace Dw3Production.Diagnostics;

/// <summary>
/// Proves that the native SQLite library loads and works inside the exported game, without any
/// disc or data pack: it writes one row to a temporary database, reads it back and deletes the
/// file. Selected with <c>DW3_SQLITE_PROBE=1</c>.
/// </summary>
internal static class SqliteProbe
{
    /// <summary>Value written and expected back.</summary>
    private const string ProbeValue = "sqlite-probe-ok";

    /// <summary>Runs the round trip and ends the session: code 0 on a match, 3 on any failure.</summary>
    /// <param name="quit">Ends the session with an exit code.</param>
    public static void Run(Action<int> quit)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"dw3_sqlite_probe_{Guid.NewGuid():N}.db");
        try
        {
            Write(dbPath);
            var readBack = Read(dbPath);
            if (readBack != ProbeValue)
            {
                throw new InvalidOperationException($"sqlite probe mismatch: expected={ProbeValue} actual={readBack}");
            }

            QuietLog.Info($"sqlite probe PASS value={readBack}");
            quit(0);
        }
        catch (Exception ex)
        {
            GD.PushError($"sqlite probe FAIL: {ex.Message}");
            quit(3);
        }
        finally
        {
            DeleteQuietly(dbPath);
        }
    }

    /// <summary>Creates the probe table and writes the probe row.</summary>
    /// <param name="dbPath">Temporary database path.</param>
    private static void Write(string dbPath)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath};Pooling=False");
        conn.Open();
        using var create = conn.CreateCommand();
        create.CommandText = "CREATE TABLE probe (key TEXT PRIMARY KEY, value TEXT NOT NULL)";
        create.ExecuteNonQuery();
        using var insert = conn.CreateCommand();
        insert.CommandText = "INSERT INTO probe (key, value) VALUES ('run', @v)";
        insert.Parameters.AddWithValue("@v", ProbeValue);
        insert.ExecuteNonQuery();
    }

    /// <summary>Reads the probe row back through a new connection.</summary>
    /// <param name="dbPath">Temporary database path.</param>
    /// <returns>The stored value.</returns>
    /// <exception cref="InvalidOperationException">The row is missing.</exception>
    private static string Read(string dbPath)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath};Pooling=False");
        conn.Open();
        using var select = conn.CreateCommand();
        select.CommandText = "SELECT value FROM probe WHERE key = 'run'";
        return (string)(select.ExecuteScalar() ?? throw new InvalidOperationException("probe row not found"));
    }

    /// <summary>Deletes the temporary database; a failure only leaves a file in the temporary directory.</summary>
    /// <param name="dbPath">Temporary database path.</param>
    private static void DeleteQuietly(string dbPath)
    {
        try
        {
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
        catch (IOException)
        {
            // Best effort: the operating system clears its temporary directory.
        }
        catch (UnauthorizedAccessException)
        {
            // Same as above: a locked or protected file is left behind.
        }
    }
}
