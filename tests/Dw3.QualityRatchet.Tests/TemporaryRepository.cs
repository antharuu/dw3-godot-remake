using System.Globalization;
using System.Text;

namespace Dw3.QualityRatchet.Tests;

/// <summary>
/// A throwaway repository layout with a solution, SARIF logs, Cobertura reports and a baseline,
/// all fabricated by the test.
/// </summary>
public sealed class TemporaryRepository : IDisposable
{
    private readonly List<(string Name, string RelativePath)> _projects = [];

    public TemporaryRepository()
    {
        Root = Path.Combine(Path.GetTempPath(), $"dw3-quality-ratchet-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string SarifDirectory => Path.Combine(Root, "artifacts", "quality", "sarif");

    public string CoverageDirectory => Path.Combine(Root, "artifacts", "quality", "coverage");

    public string BaselinePath => Path.Combine(Root, "tools", "quality-baseline.json");

    public string MergedCoveragePath => Path.Combine(Root, "artifacts", "quality", "coverage.cobertura.xml");

    public static string SarifResult(string rule, string absoluteFile, int line, string level, bool suppressed)
    {
        string uri = new Uri(absoluteFile).AbsoluteUri;
        string suppressions = suppressed ? ",\"suppressions\":[{\"kind\":\"inSource\"}]" : string.Empty;
        return "{\"ruleId\":\"" + rule + "\",\"level\":\"" + level + "\"" + suppressions
            + ",\"locations\":[{\"physicalLocation\":{\"artifactLocation\":{\"uri\":\"" + uri + "\"},"
            + "\"region\":{\"startLine\":" + line.ToString(CultureInfo.InvariantCulture)
            + ",\"startColumn\":1}}}]}";
    }

    public void AddProject(string name, string relativeDirectory)
    {
        _projects.Add((name, $"{relativeDirectory}/{name}.csproj"));
        Directory.CreateDirectory(Path.Combine(Root, relativeDirectory));
        WriteSolution();
    }

    public string ProjectDirectory(string name)
    {
        string relative = _projects.Single(project => project.Name == name).RelativePath;
        return Path.GetDirectoryName(Path.Combine(Root, relative))!;
    }

    public void WriteSarif(string project, params (string Rule, string RelativeFile, int Line)[] results)
    {
        var entries = results.Select(result => SarifResult(
            result.Rule,
            Path.Combine(ProjectDirectory(project), result.RelativeFile),
            result.Line,
            "warning",
            suppressed: false));
        WriteSarifResults(project, entries);
    }

    public void WriteSarifResults(string project, IEnumerable<string> resultJson)
    {
        Directory.CreateDirectory(SarifDirectory);
        string content = "{\"version\":\"2.1.0\",\"runs\":[{\"results\":[" + string.Join(",", resultJson) + "]}]}";
        File.WriteAllText(Path.Combine(SarifDirectory, project + ".sarif"), content);
    }

    /// <summary>
    /// Writes a minimal Cobertura report without a &lt;source&gt; element (raw filenames are used as-is).
    /// </summary>
    /// <param name="run">The sub-directory name inside the coverage directory.</param>
    /// <param name="package">The package (assembly) name.</param>
    /// <param name="file">The filename attribute value for the class.</param>
    /// <param name="lines">The line numbers and their hit counts.</param>
    public void WriteCobertura(string run, string package, string file, params (int Line, int Hits)[] lines)
    {
        WriteCoberturaCore(run, package, sourceDir: null, file, lines);
    }

    /// <summary>
    /// Writes a minimal Cobertura report with a &lt;source&gt; element.
    /// Used to test that reports with different source roots for the same file are merged into one entry.
    /// </summary>
    /// <param name="run">The sub-directory name inside the coverage directory.</param>
    /// <param name="package">The package (assembly) name.</param>
    /// <param name="sourceDir">The absolute path written as the &lt;source&gt; element.</param>
    /// <param name="file">The filename attribute value for the class (relative to <paramref name="sourceDir"/>).</param>
    /// <param name="lines">The line numbers and their hit counts.</param>
    public void WriteCoberturaWithSource(string run, string package, string sourceDir, string file, params (int Line, int Hits)[] lines)
    {
        WriteCoberturaCore(run, package, sourceDir, file, lines);
    }

    public void WriteBaseline(string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(BaselinePath)!);
        File.WriteAllText(BaselinePath, json);
    }

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }

    /// <summary>
    /// Internal helper that emits a minimal Cobertura XML report to disk.
    /// </summary>
    /// <param name="run">The sub-directory name.</param>
    /// <param name="package">The package name.</param>
    /// <param name="sourceDir">The &lt;source&gt; path, or null to omit the element.</param>
    /// <param name="file">The filename attribute.</param>
    /// <param name="lines">Line hit data.</param>
    private void WriteCoberturaCore(string run, string package, string? sourceDir, string file, (int Line, int Hits)[] lines)
    {
        var builder = new StringBuilder();
        builder.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?><coverage>");
        if (sourceDir is not null)
        {
            builder.Append("<sources><source>").Append(sourceDir).Append("</source></sources>");
        }

        builder.Append("<packages>");
        builder.Append("<package name=\"").Append(package).Append("\"><classes>");
        builder.Append("<class name=\"Sample\" filename=\"").Append(file).Append("\"><methods /><lines>");
        foreach ((int line, int hits) in lines)
        {
            builder.Append(CultureInfo.InvariantCulture, $"<line number=\"{line}\" hits=\"{hits}\" branch=\"False\" />");
        }

        builder.Append("</lines></class></classes></package></packages></coverage>");
        string directory = Path.Combine(CoverageDirectory, run);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "coverage.cobertura.xml"), builder.ToString());
    }

    private void WriteSolution()
    {
        var builder = new StringBuilder();
        builder.AppendLine("Microsoft Visual Studio Solution File, Format Version 12.00");
        builder.AppendLine("Project(\"{2150E333-8FDC-42A3-9474-1A3956D46DE8}\") = \"src\", \"src\", \"{00000000-0000-0000-0000-000000000001}\"");
        builder.AppendLine("EndProject");
        foreach ((string name, string relativePath) in _projects)
        {
            builder.AppendLine(
                "Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"" + name + "\", \""
                + relativePath.Replace('/', '\\') + "\", \"{00000000-0000-0000-0000-000000000002}\"");
            builder.AppendLine("EndProject");
        }

        File.WriteAllText(Path.Combine(Root, "Dw3.sln"), builder.ToString());
    }
}
