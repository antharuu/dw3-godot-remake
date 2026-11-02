using Microsoft.Data.Sqlite;

namespace Dw3.Data;

/// <summary>
/// Supplies a transaction and staging paths to extractor code writing one data pack.
/// </summary>
public sealed class DataPackWriteContext
{
    private readonly string _stagingDirectory;

    /// <summary>
    /// Initializes a new instance of the <see cref="DataPackWriteContext"/> class, binding the
    /// active database connection, transaction, and staging directory for this extraction pass.
    /// </summary>
    /// <param name="database">Open SQLite connection to the staging database.</param>
    /// <param name="transaction">Active transaction that must wrap all writes.</param>
    /// <param name="stagingDirectory">Absolute path of the staging directory receiving extracted files.</param>
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

    /// <summary>
    /// Resolves a relative image path to an absolute path inside the staging directory,
    /// after normalizing separators and rejecting path-escaping segments.
    /// </summary>
    /// <param name="relativePath">Slash-separated path beneath the pack root.</param>
    /// <returns>The absolute path inside the current staging directory.</returns>
    private string ResolveRelativePath(string relativePath)
    {
        string normalizedPath = NormalizeRelativePath(relativePath);
        return Path.Combine(_stagingDirectory, normalizedPath.Replace('/', Path.DirectorySeparatorChar));
    }

    /// <summary>
    /// Normalizes a pack-relative image path to forward slashes, rejecting absolute paths,
    /// path-traversal sequences, empty segments, and backslash-only representations.
    /// Called by both <see cref="ResolveRelativePath"/> and <see cref="RegisterImage"/>.
    /// </summary>
    /// <param name="relativePath">Raw image path as provided by the extractor caller.</param>
    /// <returns>The normalized path using forward slashes, guaranteed to be a safe relative path.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="relativePath"/> is null, empty, whitespace, rooted, or contains
    /// path-traversal (<c>..</c>) or empty path segments.
    /// </exception>
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
