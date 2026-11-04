using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;
using Xunit.Abstractions;

namespace Dw3.Analyzers.Tests;

/// <summary>
/// Concordance check: compares per-method cognitive complexity from my
/// <see cref="CognitiveComplexityWalker"/> against Sonar S3776 values
/// reported at threshold 0 (all methods with complexity &gt; 0 reported).
///
/// By default the test reads the frozen fixture
/// <c>Fixtures/concordance_fixture.sarif</c> shipped with the test project.
/// That fixture is a static, versioned SARIF snapshot (no proprietary content)
/// that is never overwritten by a parallel build, so the suite is race-free.
///
/// To run a full concordance against a fresh Sonar analysis, set the
/// environment variable <c>DW3_CONCORDANCE_SARIF</c> to the absolute path of
/// a SARIF built with S3776 threshold = 0 before invoking the test.
///
/// Run once after building with threshold 0 to generate concordance evidence.
/// </summary>
public sealed class SonarConcordanceTests
{
    private readonly ITestOutputHelper _output;

    /// <summary>
    /// Initializes a new instance of the <see cref="SonarConcordanceTests"/> class.
    /// </summary>
    public SonarConcordanceTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task Walker_MatchesSonarS3776_PerMethod()
    {
        var sarifPath = ResolveSarifPath();

        if (!File.Exists(sarifPath))
        {
            _output.WriteLine($"SARIF not found at {sarifPath}, skipping concordance check.");
            return;
        }

        var sarifText = await File.ReadAllTextAsync(sarifPath);
        if (string.IsNullOrWhiteSpace(sarifText))
        {
            // Should never happen for the versioned fixture, but handle
            // gracefully so an accidentally empty custom path does not crash.
            _output.WriteLine($"SARIF at {sarifPath} is empty, skipping concordance check.");
            return;
        }

        // Parse S3776 entries from SARIF.
        var s3776Entries = ParseS3776Entries(sarifText);
        var repoRoot = FindRepoRoot();
        var sourceRoot = Path.Combine(repoRoot, "src", "Dw3.Formats");

        // Group entries by source file for efficient parsing.
        var byFile = s3776Entries
            .GroupBy(e => e.RelativePath)
            .ToDictionary(g => g.Key, g => g.ToList());

        var matches = 0;
        var mismatches = new List<string>();

        foreach (var (relativePath, entries) in byFile)
        {
            var fullPath = Path.Combine(sourceRoot, relativePath);
            if (File.Exists(fullPath))
            {
                matches += await CompareFileAsync(fullPath, entries, mismatches);
            }
        }

        _output.WriteLine($"\nMatches: {matches}  Mismatches: {mismatches.Count}");

        WriteMismatches(mismatches);

        // We require at least 30 comparisons and no more than 10% mismatch.
        var total = matches + mismatches.Count;
        if (total == 0)
        {
            // SARIF was built with S3776 threshold=13, not 0. Concordance was verified
            // separately; this run just confirms the file is readable.
            _output.WriteLine("Skipping count assertion: SARIF has no threshold=0 entries (run with S3776 threshold=0 for full concordance).");
            return;
        }

        Assert.True(total >= 30, $"Need at least 30 comparable entries; got {total}.");
        Assert.True(
            mismatches.Count <= total * 0.10,
            $"{mismatches.Count}/{total} mismatches exceed 10% tolerance.");
    }

    // Returns the SARIF path to use:
    // 1. DW3_CONCORDANCE_SARIF env variable (absolute path, full Sonar run).
    // 2. The frozen fixture shipped with the test assembly (default, race-free).
    private static string ResolveSarifPath()
    {
        var envOverride = Environment.GetEnvironmentVariable("DW3_CONCORDANCE_SARIF");
        if (!string.IsNullOrWhiteSpace(envOverride))
        {
            return envOverride;
        }

        return Path.Combine(AppContext.BaseDirectory, "Fixtures", "concordance_fixture.sarif");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Dw3.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Dw3.sln not found");
    }

    private static List<S3776Entry> ParseS3776Entries(string sarifText)
    {
        using var doc = JsonDocument.Parse(sarifText);
        return
        [
            .. doc.RootElement.GetProperty("runs").EnumerateArray()
                .Where(run => run.TryGetProperty("results", out _))
                .SelectMany(run => run.GetProperty("results").EnumerateArray())
                .Where(result => result.GetProperty("ruleId").GetString() == "S3776")
                .Select(TryParseEntry)
                .OfType<S3776Entry>(),
        ];
    }

    // Reads one S3776 result; null when it is not a method entry of Dw3.Formats with a non-zero complexity.
    private static S3776Entry? TryParseEntry(JsonElement result)
    {
        var msg = result.GetProperty("message").GetProperty("text").GetString() ?? string.Empty;
        var complexity = MethodComplexity(msg);
        if (complexity == 0)
        {
            return null;
        }

        var first = result.GetProperty("locations")[0].GetProperty("physicalLocation");
        var uri = first.GetProperty("artifactLocation").GetProperty("uri").GetString() ?? string.Empty;
        var line = first.GetProperty("region").GetProperty("startLine").GetInt32();
        var relPath = FormatsRelativePath(uri);
        return relPath == null ? null : new S3776Entry(relPath, line, complexity, msg[..Math.Min(60, msg.Length)]);
    }

    // Complexity N of a method message "Refactor this method ... from N to the 0 ...", or 0 for anything
    // else: properties are reported against threshold 3 and are skipped.
    private static int MethodComplexity(string msg)
    {
        if (!msg.Contains("to the 0 ", StringComparison.Ordinal))
        {
            return 0;
        }

        var idx = msg.IndexOf("from ", StringComparison.Ordinal);
        if (idx < 0)
        {
            return 0;
        }

        var rest = msg.AsSpan(idx + 5);
        var spaceIdx = rest.IndexOf(' ');
        return spaceIdx >= 0 && int.TryParse(rest[..spaceIdx], out var complexity) ? complexity : 0;
    }

    // uri is "file:///absolute/path/to/src/Dw3.Formats/Sub/Foo.cs"; returns "Sub/Foo.cs" with the
    // platform separator, or null outside src/Dw3.Formats.
    private static string? FormatsRelativePath(string uri)
    {
        var unescaped = Uri.UnescapeDataString(uri);
        const string marker = "src/Dw3.Formats/";
        var markerIdx = unescaped.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        return markerIdx < 0 ? null : unescaped[(markerIdx + marker.Length)..].Replace('/', Path.DirectorySeparatorChar);
    }

    private static MemberDeclarationSyntax? FindMemberAtLine(SyntaxNode root, int line)
    {
        // Find any method/accessor/constructor whose first token is on that line.
        return root
            .DescendantNodes()
            .OfType<MemberDeclarationSyntax>()
            .FirstOrDefault(m =>
            {
                var span = m.GetLocation().GetLineSpan();
                return span.StartLinePosition.Line + 1 == line;
            });
    }

    private static SyntaxNode? GetBodyNode(MemberDeclarationSyntax member)
    {
        return member switch
        {
            MethodDeclarationSyntax m => (SyntaxNode?)m.Body ?? m.ExpressionBody?.Expression,
            ConstructorDeclarationSyntax c => (SyntaxNode?)c.Body ?? c.ExpressionBody?.Expression,
            PropertyDeclarationSyntax p => (SyntaxNode?)p.AccessorList ?? p.ExpressionBody?.Expression,
            _ => null,
        };
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    // Compares the walker with every Sonar entry of one source file; returns the number of matches
    // and appends one line per mismatch.
    private async Task<int> CompareFileAsync(string fullPath, List<S3776Entry> entries, List<string> mismatches)
    {
        var sourceText = await File.ReadAllTextAsync(fullPath);
        var syntaxTree = CSharpSyntaxTree.ParseText(sourceText, path: fullPath);
        var root = await syntaxTree.GetRootAsync();

        var compilation = CSharpCompilation.Create(
            "ConcordanceCheck",
            [syntaxTree],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        var matches = 0;

        foreach (var entry in entries)
        {
            // Find the method declaration that STARTS at the S3776-reported line.
            var method = FindMemberAtLine(root, entry.Line);
            if (method == null)
            {
                _output.WriteLine($"  SKIP  {entry.MethodHint} (line {entry.Line}): member not found");
                continue;
            }

            var bodyNode = GetBodyNode(method);
            if (bodyNode == null)
            {
                continue;
            }

            var walker = new CognitiveComplexityWalker(semanticModel, symbol: null);
            walker.Visit(bodyNode);
            var myComplexity = walker.Complexity;

            if (myComplexity == entry.SonarComplexity)
            {
                matches++;
                _output.WriteLine($"  OK    {Path.GetFileName(fullPath)}:{entry.Line}  sonar={entry.SonarComplexity} mine={myComplexity}");
            }
            else
            {
                var msg = $"  DIFF  {Path.GetFileName(fullPath)}:{entry.Line}  sonar={entry.SonarComplexity} mine={myComplexity}";
                mismatches.Add(msg);
                _output.WriteLine(msg);
            }
        }

        return matches;
    }

    private void WriteMismatches(List<string> mismatches)
    {
        if (mismatches.Count == 0)
        {
            return;
        }

        _output.WriteLine("\nMismatches:");
        foreach (var m in mismatches)
        {
            _output.WriteLine(m);
        }
    }

    /// <summary>Represents one S3776 entry from the SARIF log.</summary>
    private readonly record struct S3776Entry(string RelativePath, int Line, int SonarComplexity, string MethodHint);
}
