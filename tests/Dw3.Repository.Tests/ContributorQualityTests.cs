using System.Text.RegularExpressions;
using Xunit;

namespace Dw3.Repository.Tests;

public sealed class ContributorQualityTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    [Fact]
    public void EditorConfig_EnforcesCSharpStyleBaseline()
    {
        string path = Path.Combine(RepoRoot, ".editorconfig");
        Assert.True(File.Exists(path), "A root .editorconfig is required for C# contributors.");

        string content = File.ReadAllText(path);
        Assert.Contains("root = true", content, StringComparison.Ordinal);
        Assert.Contains("[*.cs]", content, StringComparison.Ordinal);
        Assert.Contains("indent_size = 4", content, StringComparison.Ordinal);
        Assert.Contains("dotnet_diagnostic.IDE0005.severity = warning", content, StringComparison.Ordinal);
        Assert.Contains("dotnet_diagnostic.IDE0059.severity = warning", content, StringComparison.Ordinal);
        Assert.Contains("dotnet_diagnostic.IDE0060.severity = warning", content, StringComparison.Ordinal);
        Assert.Contains("dotnet_diagnostic.IDE0031.severity = warning", content, StringComparison.Ordinal);
        Assert.Contains("dotnet_diagnostic.IDE0028.severity = warning", content, StringComparison.Ordinal);
        Assert.Contains("dotnet_diagnostic.IDE0300.severity = warning", content, StringComparison.Ordinal);
        Assert.Contains("dotnet_diagnostic.IDE0301.severity = warning", content, StringComparison.Ordinal);
        Assert.Contains("dotnet_diagnostic.IDE0305.severity = warning", content, StringComparison.Ordinal);
        Assert.Contains("dotnet_diagnostic.IDE0058.severity = none", content, StringComparison.Ordinal);
        Assert.Contains("dotnet_diagnostic.IDE0290.severity = none", content, StringComparison.Ordinal);
    }

    [Fact]
    public void GameSource_UsesNoMoreThanHalfTheOriginalNullForgivingBaseline()
    {
        string gameSource = Path.Combine(RepoRoot, "src", "Dw3.Game");
        int count = Directory.EnumerateFiles(gameSource, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(
                part => part is "bin" or "obj" or ".godot"))
            .Sum(CountPostfixNullForgivingOperators);

        Assert.True(count <= 64,
            $"Expected at most 64 postfix null-forgiving operators in Dw3.Game, found {count}.");
    }

    private static int CountPostfixNullForgivingOperators(string path)
    {
        string source = File.ReadAllText(path);
        return Regex.Count(source, @"(?:(?<=\w)|(?<=[\)\]\}]))!(?=\s*(?:[.\[;,\)\]\}:]))");
    }

    private static string FindRepoRoot()
    {
        string? directory = AppContext.BaseDirectory;
        while (directory is not null)
        {
            string gitPath = Path.Combine(directory, ".git");
            if (Directory.Exists(gitPath) || File.Exists(gitPath))
            {
                return directory;
            }

            directory = Path.GetDirectoryName(directory);
        }

        throw new InvalidOperationException("Cannot locate the repository root (.git/).");
    }
}
