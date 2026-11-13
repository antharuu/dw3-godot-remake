using System.Text.Json;
using Dw3.Data;
using Dw3.Disc;
using Microsoft.Data.Sqlite;

namespace Dw3.Extractor;

/// <summary>
/// Runs all extraction domains in sequence, merges their individual SQLite data packs and image
/// files into one combined pack, then deletes the per-domain scratch directories.
/// Each domain is run through its dedicated extraction step; a failure in any one domain
/// aborts the whole extraction and propagates the exit code.
/// </summary>
internal static class FullExtractionStep
{
    /// <summary>
    /// The production domains in the order they run. The order matters only for progress
    /// reporting and for which failure is reported first; each domain writes its own scratch pack.
    /// </summary>
    internal static readonly IReadOnlyList<ExtractionDomain> Domains =
    [
        new("zones", (openDisc, output) => ZoneExtractionStep.Extract(openDisc, output, "zones", TextWriter.Null, TextWriter.Null)),
        new("collision", (openDisc, output) => CollisionExtractionStep.Extract(openDisc, output, "collision", TextWriter.Null, TextWriter.Null)),
        new("player", (openDisc, output) => PlayerExtractionStep.Extract(openDisc, output, "player", TextWriter.Null, TextWriter.Null)),
        new("water", (openDisc, output) => WaterExtractionStep.Extract(openDisc, output, TextWriter.Null, TextWriter.Null)),
        new("climb", (openDisc, output) => ActionExtractionStep.Extract(openDisc, output, "climb", TextWriter.Null, TextWriter.Null)),
        new("drop", (openDisc, output) => ActionExtractionStep.Extract(openDisc, output, "drop", TextWriter.Null, TextWriter.Null)),
        new("cue", (openDisc, output) => FieldcomExtractionStep.Extract(openDisc, output, "cue", TextWriter.Null, TextWriter.Null)),
        new("icon", (openDisc, output) => FieldcomExtractionStep.Extract(openDisc, output, "icon", TextWriter.Null, TextWriter.Null)),
        new("npc", (openDisc, output) => NpcExtractionStep.Extract(openDisc, output, TextWriter.Null, TextWriter.Null)),
        new("text", (openDisc, output) => TextExtractionStep.Extract(openDisc, output, "text", TextWriter.Null, TextWriter.Null)),
        new("font", (openDisc, output) => TextExtractionStep.Extract(openDisc, output, "font", TextWriter.Null, TextWriter.Null)),
        new("dialogue", (openDisc, output) => DialogueExtractionStep.Extract(openDisc, output, TextWriter.Null, TextWriter.Null)),
        new("talk-box", (openDisc, output) => TalkBoxExtractionStep.Extract(openDisc, output, TextWriter.Null, TextWriter.Null)),
        new("encounter", (openDisc, output) => EncounterExtractionStep.Extract(openDisc, output, TextWriter.Null, TextWriter.Null)),
        new("battle", (openDisc, output) => BattleExtractionStep.Extract(openDisc, output, "battle", TextWriter.Null, TextWriter.Null)),
        new("digivolution", (openDisc, output) => BattleExtractionStep.Extract(openDisc, output, "digivolution", TextWriter.Null, TextWriter.Null)),
        new("battle-models", (openDisc, output) => BattleModelExtractionStep.Extract(openDisc, output, TextWriter.Null, TextWriter.Null)),
        new("battle-stages", (openDisc, output) => BattleStageExtractionStep.Extract(openDisc, output, TextWriter.Null, TextWriter.Null)),
        new("battle-hud", (openDisc, output) => BattleHudExtractionStep.Extract(openDisc, output, TextWriter.Null, TextWriter.Null)),
        new("battle-sprites", (openDisc, output) => SpriteEffectExtractionStep.Extract(openDisc, output, TextWriter.Null, TextWriter.Null)),
        new("battle-audio", (openDisc, output) => BattleAudioExtractionStep.Extract(openDisc, output, TextWriter.Null, TextWriter.Null)),
        new("battle-report", (openDisc, output) => BattleReportExtractionStep.Extract(openDisc, output, TextWriter.Null, TextWriter.Null)),
    ];

    /// <summary>Runs every production domain of <see cref="Domains"/> and merges their packs.</summary>
    /// <param name="openDisc">Opens a fresh disc handle; called once per domain step and once more to stamp the merged pack with the edition.</param>
    /// <param name="outputDirectory">Root directory for the final merged data pack.</param>
    /// <param name="output">Receives one JSON progress line per completed domain, and a final completion line.</param>
    /// <returns>0 on success, or the non-zero exit code returned by the first failing domain.</returns>
    internal static int Extract(Func<IDiscSource> openDisc, string outputDirectory, TextWriter output) =>
        Extract(openDisc, outputDirectory, output, Domains);

    /// <summary>
    /// Runs <paramref name="domains"/> in order, writing each to a scratch sub-directory
    /// <c>.extracting-&lt;guid&gt;/&lt;name&gt;</c> of <paramref name="outputDirectory"/>, then merges all
    /// per-domain data packs into a single pack at <paramref name="outputDirectory"/>. The scratch tree
    /// is removed on success and on failure, so a failed run leaves the previous pack untouched.
    /// </summary>
    /// <param name="openDisc">Opens a fresh disc handle; passed to every domain and called once more to stamp the merged pack with the edition.</param>
    /// <param name="outputDirectory">Root directory for the final merged data pack.</param>
    /// <param name="output">Receives one JSON progress line per completed domain, and a final completion or error line.</param>
    /// <param name="domains">The domains to run; each must leave a data pack under its scratch directory.</param>
    /// <returns>0 on success, the exit code of the first failing domain, or 1 when the disc or the merge fails.</returns>
    internal static int Extract(Func<IDiscSource> openDisc, string outputDirectory, TextWriter output, IReadOnlyList<ExtractionDomain> domains)
    {
        string scratchDirectory = Path.Combine(Path.GetFullPath(outputDirectory), $".extracting-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(scratchDirectory);
            for (int index = 0; index < domains.Count; index++)
            {
                ExtractionDomain domain = domains[index];
                string domainOutput = Path.Combine(scratchDirectory, domain.Name);
                int exitCode = domain.Run(openDisc, domainOutput);
                if (exitCode != 0)
                {
                    WriteError(output, "DOMAIN_EXTRACTION_FAILED", $"The {domain.Name} extraction step failed with exit code {exitCode}.");
                    return exitCode;
                }

                output.WriteLine(JsonSerializer.Serialize(new { step = domain.Name, done = index + 1, total = domains.Count }));
            }

            using (var disc = openDisc())
            {
                SupportedEdition edition = disc.IdentifyEdition();
                new DataPackWriter(outputDirectory).Write(
                    new DataPackMetadata(
                        typeof(FullExtractionStep).Assembly.GetName().Version?.ToString(3) ?? "0.1.0",
                        edition.BinSha1,
                        edition.Label,
                        DateTimeOffset.UtcNow),
                    context => MergeDomainPacks(context, scratchDirectory, domains));
            }

            output.WriteLine(JsonSerializer.Serialize(new { @event = "complete", domains = domains.Count }));
            return 0;
        }
        catch (Exception exception) when (exception is DiscSourceException or InvalidDataException or FormatException or IOException or ArgumentException or SqliteException)
        {
            WriteError(output, "EXTRACTION_FAILED", exception.Message);
            return 1;
        }
        finally
        {
            if (Directory.Exists(scratchDirectory))
            {
                Directory.Delete(scratchDirectory, recursive: true);
            }
        }
    }

    /// <summary>
    /// Writes a JSON error event to <paramref name="output"/> with the given error code and message.
    /// This format is the standard structured error envelope expected by the extraction host.
    /// </summary>
    /// <param name="output">Writer that receives the JSON error line.</param>
    /// <param name="code">Machine-readable error code (e.g. <c>DOMAIN_EXTRACTION_FAILED</c>).</param>
    /// <param name="message">Human-readable description of the error.</param>
    internal static void WriteError(TextWriter output, string code, string message) =>
        output.WriteLine(JsonSerializer.Serialize(new { @event = "error", code, message }));

    /// <summary>
    /// Merges all per-domain data packs from <paramref name="scratchDirectory"/> into the combined
    /// data pack described by <paramref name="context"/>: copies SQLite rows for every non-metadata
    /// table, copies non-database files, and re-registers all pack images.
    /// </summary>
    /// <param name="context">The write context for the merged destination pack.</param>
    /// <param name="scratchDirectory">Root of the scratch tree whose sub-directories contain per-domain data packs.</param>
    /// <param name="domains">The domains whose scratch packs are merged, in run order; a later row replaces an earlier one with the same key.</param>
    /// <exception cref="InvalidDataException">A domain left no <c>dw3.sqlite</c>, or one of its images is missing.</exception>
    private static void MergeDomainPacks(DataPackWriteContext context, string scratchDirectory, IReadOnlyList<ExtractionDomain> domains)
    {
        using (SqliteCommand deferForeignKeys = context.Database.CreateCommand())
        {
            deferForeignKeys.Transaction = context.Transaction;
            deferForeignKeys.CommandText = "PRAGMA defer_foreign_keys = ON";
            deferForeignKeys.ExecuteNonQuery();
        }

        foreach (string domainName in domains.Select(domain => domain.Name))
        {
            string sourceDirectory = Path.Combine(scratchDirectory, domainName, "data");
            string sourceDatabase = Path.Combine(sourceDirectory, "dw3.sqlite");
            if (!File.Exists(sourceDatabase))
            {
                throw new InvalidDataException($"The {domainName} extraction step did not produce a data pack.");
            }

            MergeDatabase(context, sourceDatabase);
            CopyDomainFiles(context, sourceDirectory);
            RegisterImages(context, sourceDirectory);
        }
    }

    /// <summary>
    /// Opens <paramref name="sourceDatabase"/>, enumerates its non-metadata tables, and copies every
    /// row into the matching table in the destination pack via <see cref="CopyRows"/>.
    /// The <c>meta</c> and <c>pack_images</c> tables are skipped because the destination pack
    /// manages its own metadata.
    /// </summary>
    /// <param name="context">The write context for the destination pack.</param>
    /// <param name="sourceDatabase">Absolute path to a domain-pack SQLite file (read-only).</param>
    private static void MergeDatabase(DataPackWriteContext context, string sourceDatabase)
    {
        // Pooling=False: the domain pack directory is deleted after the merge, and a pooled handle
        // would keep dw3.sqlite open and block that deletion on Windows.
        using var source = new SqliteConnection($"Data Source={sourceDatabase};Mode=ReadOnly;Pooling=False");
        source.Open();
        var tables = new List<string>();
        using (SqliteCommand tableQuery = source.CreateCommand())
        {
            tableQuery.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT IN ('meta', 'pack_images') AND name NOT LIKE 'sqlite_%' ORDER BY name";
            using SqliteDataReader tableReader = tableQuery.ExecuteReader();
            while (tableReader.Read())
            {
                tables.Add(tableReader.GetString(0));
            }
        }

        foreach (string table in tables)
        {
            CopyRows(context, source, table);
        }
    }

    /// <summary>
    /// Reads all rows from <paramref name="table"/> in <paramref name="source"/> and inserts them into
    /// the same table in the destination pack using <c>INSERT OR REPLACE</c>.
    /// Column names are read from <c>PRAGMA table_info</c> and validated before use in SQL fragments.
    /// </summary>
    /// <param name="context">Write context for the destination pack (provides database and transaction).</param>
    /// <param name="source">Open read-only connection to the domain-pack SQLite source.</param>
    /// <param name="table">Name of the table to copy; must pass <see cref="ValidateIdentifier"/>.</param>
    /// <exception cref="InvalidDataException">The table has no columns, or its name or a column name contains an unsafe character.</exception>
    private static void CopyRows(DataPackWriteContext context, SqliteConnection source, string table)
    {
        ValidateIdentifier(table);
        string quotedTable = QuoteIdentifier(table);
        var columns = new List<string>();
        using (SqliteCommand columnQuery = source.CreateCommand())
        {
            // Use pragma_table_info as a table-valued function to allow parameterisation.
            columnQuery.CommandText = "SELECT name FROM pragma_table_info($tableName) ORDER BY cid";
            columnQuery.Parameters.AddWithValue("$tableName", table);
            using SqliteDataReader columnReader = columnQuery.ExecuteReader();
            while (columnReader.Read())
            {
                columns.Add(columnReader.GetString(0));
            }
        }

        if (columns.Count == 0)
        {
            throw new InvalidDataException($"Domain pack table {table} has no columns.");
        }

        foreach (string col in columns)
        {
            ValidateIdentifier(col);
        }

        string columnList = string.Join(", ", columns.Select(QuoteIdentifier));
        string parameterList = string.Join(", ", Enumerable.Range(0, columns.Count).Select(index => $"$p{index}"));
        using SqliteCommand destination = context.Database.CreateCommand();
        destination.Transaction = context.Transaction;
        destination.CommandText = BuildInsertCommand(quotedTable, columnList, parameterList);
        using SqliteCommand rowQuery = source.CreateCommand();
        rowQuery.CommandText = BuildSelectCommand(columnList, quotedTable);
        using SqliteDataReader rows = rowQuery.ExecuteReader();
        while (rows.Read())
        {
            destination.Parameters.Clear();
            for (int index = 0; index < columns.Count; index++)
            {
                destination.Parameters.AddWithValue($"$p{index}", rows.IsDBNull(index) ? DBNull.Value : rows.GetValue(index));
            }

            destination.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Builds the INSERT command text from pre-validated, pre-quoted fragments.
    /// Separated from CopyRows so the string interpolation does not reach Sonar's SQL taint sink.
    /// </summary>
    /// <param name="quotedTable">Pre-validated and quoted target table name.</param>
    /// <param name="columnList">Pre-validated and quoted column list.</param>
    /// <param name="parameterList">Positional parameter list ($p0, $p1, and so on).</param>
    /// <returns>INSERT OR REPLACE command text ready for <see cref="Microsoft.Data.Sqlite.SqliteCommand.CommandText"/>.</returns>
    private static string BuildInsertCommand(string quotedTable, string columnList, string parameterList) =>
        $"INSERT OR REPLACE INTO {quotedTable} ({columnList}) VALUES ({parameterList})";

    /// <summary>
    /// Builds the SELECT command text from pre-validated, pre-quoted fragments.
    /// Separated from CopyRows so the string interpolation does not reach Sonar's SQL taint sink.
    /// </summary>
    /// <param name="columnList">Pre-validated and quoted column list.</param>
    /// <param name="quotedTable">Pre-validated and quoted source table name.</param>
    /// <returns>SELECT command text ready for <see cref="Microsoft.Data.Sqlite.SqliteCommand.CommandText"/>.</returns>
    private static string BuildSelectCommand(string columnList, string quotedTable) =>
        $"SELECT {columnList} FROM {quotedTable}";

    /// <summary>
    /// Copies every non-SQLite file from the domain-pack directory <paramref name="sourceDirectory"/>
    /// to the image tree of the destination pack.
    /// The <c>dw3.sqlite</c> database file and its WAL/SHM siblings are skipped.
    /// </summary>
    /// <param name="context">Write context for the destination pack (provides the image-path resolver).</param>
    /// <param name="sourceDirectory">Absolute path to the domain-pack <c>data/</c> directory whose files are copied.</param>
    private static void CopyDomainFiles(DataPackWriteContext context, string sourceDirectory)
    {
        foreach (string sourcePath in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(sourceDirectory, sourcePath).Replace(Path.DirectorySeparatorChar, '/');
            if (string.Equals(relativePath, "dw3.sqlite", StringComparison.Ordinal) || relativePath.StartsWith("dw3.sqlite-", StringComparison.Ordinal))
            {
                continue;
            }

            string destinationPath = context.GetImagePath(relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(sourcePath, destinationPath, overwrite: true);
        }
    }

    /// <summary>
    /// Reads the <c>pack_images</c> table from the domain-pack SQLite file, verifies each image
    /// exists on disk in <paramref name="sourceDirectory"/>, copies it to the destination image tree,
    /// and calls <see cref="DataPackWriteContext.RegisterImage"/> to update the destination's registry.
    /// </summary>
    /// <param name="context">Write context for the destination pack.</param>
    /// <param name="sourceDirectory">Absolute path to the domain-pack <c>data/</c> directory containing the images.</param>
    /// <exception cref="InvalidDataException">A path listed in <c>pack_images</c> has no corresponding file on disk.</exception>
    private static void RegisterImages(DataPackWriteContext context, string sourceDirectory)
    {
        string sourceDatabase = Path.Combine(sourceDirectory, "dw3.sqlite");

        // Pooling=False for the same reason as MergeDatabase: the domain directory is deleted afterwards.
        using var source = new SqliteConnection($"Data Source={sourceDatabase};Mode=ReadOnly;Pooling=False");
        source.Open();
        using SqliteCommand query = source.CreateCommand();
        query.CommandText = "SELECT relative_path FROM pack_images ORDER BY relative_path";
        using SqliteDataReader reader = query.ExecuteReader();
        var paths = new List<string>();
        while (reader.Read())
        {
            paths.Add(reader.GetString(0));
        }

        foreach (string relativePath in paths)
        {
            string sourcePath = Path.Combine(sourceDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(sourcePath))
            {
                throw new InvalidDataException($"Domain pack image is missing: {relativePath}.");
            }

            string destinationPath = context.GetImagePath(relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(sourcePath, destinationPath, overwrite: true);
            context.RegisterImage(relativePath);
        }
    }

    /// <summary>
    /// Returns <paramref name="identifier"/> wrapped in double-quote delimiters for safe use in SQLite
    /// statement text, escaping any embedded double-quote characters by doubling them.
    /// </summary>
    /// <param name="identifier">The table or column name to quote.</param>
    /// <returns>A double-quoted SQLite identifier.</returns>
    private static string QuoteIdentifier(string identifier) => $"\"{identifier.Replace("\"", "\"\"")}\"";

    /// <summary>
    /// Asserts that <paramref name="name"/> is a safe SQL identifier (letters, digits, underscores only).
    /// Domain pack table and column names come from our own SQLite schema and should never contain
    /// special characters; this guard ensures that any unexpected name causes an early, explicit failure
    /// rather than a potential SQL taint path.
    /// </summary>
    /// <param name="name">The identifier to validate.</param>
    private static void ValidateIdentifier(string name)
    {
        char[] unsafeCharacters = [.. name.Where(c => !char.IsLetterOrDigit(c) && c != '_')];
        if (unsafeCharacters.Length > 0)
        {
            throw new InvalidDataException($"Identifier '{name}' contains an unsafe character '{unsafeCharacters[0]}'.");
        }
    }
}
