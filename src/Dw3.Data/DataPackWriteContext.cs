using Microsoft.Data.Sqlite;

namespace Dw3.Data;

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
        {
            throw new ArgumentException("Image paths must be non-rooted paths beneath the data pack.", nameof(relativePath));
        }

        return normalizedPath;
    }
}
