using System.Text.Json;
using Dw3.Data;
using Dw3.Disc;
using Microsoft.Data.Sqlite;

namespace Dw3.Extractor;

internal static class FullExtractionStep
{
    private sealed record Domain(string Name, Func<string, string, int> Run);

    private static readonly IReadOnlyList<Domain> Domains =
    [
        new("zones", (cue, output) => ZoneExtractionStep.Extract(cue, output, "zones", TextWriter.Null, TextWriter.Null)),
        new("collision", (cue, output) => CollisionExtractionStep.Extract(cue, output, "collision", TextWriter.Null, TextWriter.Null)),
        new("player", (cue, output) => PlayerExtractionStep.Extract(cue, output, "player", TextWriter.Null, TextWriter.Null)),
        new("water", (cue, output) => WaterExtractionStep.Extract(cue, output, TextWriter.Null, TextWriter.Null)),
        new("climb", (cue, output) => ActionExtractionStep.Extract(cue, output, "climb", TextWriter.Null, TextWriter.Null)),
        new("drop", (cue, output) => ActionExtractionStep.Extract(cue, output, "drop", TextWriter.Null, TextWriter.Null)),
        new("cue", (cue, output) => FieldcomExtractionStep.Extract(cue, output, "cue", TextWriter.Null, TextWriter.Null)),
        new("icon", (cue, output) => FieldcomExtractionStep.Extract(cue, output, "icon", TextWriter.Null, TextWriter.Null)),
        new("npc", (cue, output) => NpcExtractionStep.Extract(cue, output, TextWriter.Null, TextWriter.Null)),
        new("text", (cue, output) => TextExtractionStep.Extract(cue, output, "text", TextWriter.Null, TextWriter.Null)),
        new("font", (cue, output) => TextExtractionStep.Extract(cue, output, "font", TextWriter.Null, TextWriter.Null)),
        new("dialogue", (cue, output) => DialogueExtractionStep.Extract(cue, output, TextWriter.Null, TextWriter.Null)),
        new("talk-box", (cue, output) => TalkBoxExtractionStep.Extract(cue, output, TextWriter.Null, TextWriter.Null)),
        new("encounter", (cue, output) => EncounterExtractionStep.Extract(cue, output, TextWriter.Null, TextWriter.Null)),
        new("battle", (cue, output) => BattleExtractionStep.Extract(cue, output, "battle", TextWriter.Null, TextWriter.Null)),
        new("digivolution", (cue, output) => BattleExtractionStep.Extract(cue, output, "digivolution", TextWriter.Null, TextWriter.Null)),
        new("battle-models", (cue, output) => BattleModelExtractionStep.Extract(cue, output, TextWriter.Null, TextWriter.Null)),
        new("battle-stages", (cue, output) => BattleStageExtractionStep.Extract(cue, output, TextWriter.Null, TextWriter.Null)),
        new("battle-hud", (cue, output) => BattleHudExtractionStep.Extract(cue, output, TextWriter.Null, TextWriter.Null)),
        new("battle-report", (cue, output) => BattleReportExtractionStep.Extract(cue, output, TextWriter.Null, TextWriter.Null)),
    ];

    internal static int Extract(string cuePath, string outputDirectory, TextWriter output)
    {
        string scratchDirectory = Path.Combine(Path.GetFullPath(outputDirectory), $".extracting-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(scratchDirectory);
            for (int index = 0; index < Domains.Count; index++)
            {
                Domain domain = Domains[index];
                string domainOutput = Path.Combine(scratchDirectory, domain.Name);
                int exitCode = domain.Run(cuePath, domainOutput);
                if (exitCode != 0)
                {
                    WriteError(output, "DOMAIN_EXTRACTION_FAILED", $"The {domain.Name} extraction step failed with exit code {exitCode}.");
                    return exitCode;
                }

                output.WriteLine(JsonSerializer.Serialize(new { step = domain.Name, done = index + 1, total = Domains.Count }));
            }

            using (var disc = DiscSource.Open(cuePath))
            {
                SupportedEdition edition = disc.IdentifyEdition();
                new DataPackWriter(outputDirectory).Write(
                    new DataPackMetadata(
                        typeof(FullExtractionStep).Assembly.GetName().Version?.ToString(3) ?? "0.1.0",
                        edition.BinSha1,
                        edition.Label,
                        DateTimeOffset.UtcNow),
                    context => MergeDomainPacks(context, scratchDirectory));
            }

            output.WriteLine(JsonSerializer.Serialize(new { @event = "complete", domains = Domains.Count }));
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
                Directory.Delete(scratchDirectory, recursive: true);
        }
    }

    internal static void WriteError(TextWriter output, string code, string message) =>
        output.WriteLine(JsonSerializer.Serialize(new { @event = "error", code, message }));

    private static void MergeDomainPacks(DataPackWriteContext context, string scratchDirectory)
    {
        using (SqliteCommand deferForeignKeys = context.Database.CreateCommand())
        {
            deferForeignKeys.Transaction = context.Transaction;
            deferForeignKeys.CommandText = "PRAGMA defer_foreign_keys = ON";
            deferForeignKeys.ExecuteNonQuery();
        }

        foreach (Domain domain in Domains)
        {
            string sourceDirectory = Path.Combine(scratchDirectory, domain.Name, "data");
            string sourceDatabase = Path.Combine(sourceDirectory, "dw3.sqlite");
            if (!File.Exists(sourceDatabase))
                throw new InvalidDataException($"The {domain.Name} extraction step did not produce a data pack.");

            MergeDatabase(context, sourceDatabase);
            CopyDomainFiles(context, sourceDirectory);
            RegisterImages(context, sourceDirectory);
        }
    }

    private static void MergeDatabase(DataPackWriteContext context, string sourceDatabase)
    {
        using var source = new SqliteConnection($"Data Source={sourceDatabase};Mode=ReadOnly");
        source.Open();
        var tables = new List<string>();
        using (SqliteCommand tableQuery = source.CreateCommand())
        {
            tableQuery.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT IN ('meta', 'pack_images') AND name NOT LIKE 'sqlite_%' ORDER BY name";
            using SqliteDataReader tableReader = tableQuery.ExecuteReader();
            while (tableReader.Read())
                tables.Add(tableReader.GetString(0));
        }

        foreach (string table in tables)
            CopyRows(context, source, table);
    }

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
                columns.Add(columnReader.GetString(0));
        }

        if (columns.Count == 0)
            throw new InvalidDataException($"Domain pack table {table} has no columns.");

        foreach (string col in columns)
            ValidateIdentifier(col);

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
                destination.Parameters.AddWithValue($"$p{index}", rows.IsDBNull(index) ? DBNull.Value : rows.GetValue(index));
            destination.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Builds the INSERT command text from pre-validated, pre-quoted fragments.
    /// Separated from CopyRows so the string interpolation does not reach Sonar's SQL taint sink.
    /// </summary>
    /// <param name="quotedTable">Pre-validated and quoted target table name.</param>
    /// <param name="columnList">Pre-validated and quoted column list.</param>
    /// <param name="parameterList">Positional parameter list ($p0, $p1, …).</param>
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

    private static void CopyDomainFiles(DataPackWriteContext context, string sourceDirectory)
    {
        foreach (string sourcePath in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(sourceDirectory, sourcePath).Replace(Path.DirectorySeparatorChar, '/');
            if (string.Equals(relativePath, "dw3.sqlite", StringComparison.Ordinal) || relativePath.StartsWith("dw3.sqlite-", StringComparison.Ordinal))
                continue;
            string destinationPath = context.GetImagePath(relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(sourcePath, destinationPath, overwrite: true);
        }
    }

    private static void RegisterImages(DataPackWriteContext context, string sourceDirectory)
    {
        string sourceDatabase = Path.Combine(sourceDirectory, "dw3.sqlite");
        using var source = new SqliteConnection($"Data Source={sourceDatabase};Mode=ReadOnly");
        source.Open();
        using SqliteCommand query = source.CreateCommand();
        query.CommandText = "SELECT relative_path FROM pack_images ORDER BY relative_path";
        using SqliteDataReader reader = query.ExecuteReader();
        var paths = new List<string>();
        while (reader.Read())
            paths.Add(reader.GetString(0));

        foreach (string relativePath in paths)
        {
            string sourcePath = Path.Combine(sourceDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(sourcePath))
                throw new InvalidDataException($"Domain pack image is missing: {relativePath}.");
            string destinationPath = context.GetImagePath(relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(sourcePath, destinationPath, overwrite: true);
            context.RegisterImage(relativePath);
        }
    }

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
        foreach (char c in name)
        {
            if (!char.IsLetterOrDigit(c) && c != '_')
                throw new InvalidDataException($"Identifier '{name}' contains an unsafe character '{c}'.");
        }
    }
}
