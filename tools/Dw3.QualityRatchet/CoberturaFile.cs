using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace Dw3.QualityRatchet;

/// <summary>
/// Reads and writes the Cobertura XML format produced by the coverlet collector.
/// </summary>
internal static class CoberturaFile
{
    /// <summary>
    /// The file name the coverlet collector gives to its report.
    /// </summary>
    public const string ReportFileName = "coverage.cobertura.xml";

    /// <summary>
    /// Adds the line hits of a Cobertura report to a merged report.
    /// </summary>
    /// <param name="report">The merged report.</param>
    /// <param name="path">The path of the Cobertura report.</param>
    public static void ReadInto(CoverageReport report, string path)
    {
        XDocument document = Load(path);
        foreach (XElement package in document.Descendants("package"))
        {
            string packageName = RequiredAttribute(package, "name", path);
            foreach (XElement classElement in package.Descendants("class"))
            {
                AddClassLines(report, packageName, classElement, path);
            }
        }
    }

    /// <summary>
    /// Writes a merged report as a Cobertura document with one package per library.
    /// </summary>
    /// <param name="report">The merged report.</param>
    /// <param name="path">The output path. Its directory is created when missing.</param>
    public static void Write(CoverageReport report, string path)
    {
        var packages = new XElement("packages", report.Packages.Select(package => PackageElement(report, package)));
        var document = new XDocument(new XElement("coverage", new XAttribute("version", "1"), packages));
        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        document.Save(path);
    }

    /// <summary>
    /// Parses a report file.
    /// </summary>
    /// <param name="path">The path of the report.</param>
    /// <returns>The parsed document.</returns>
    private static XDocument Load(string path)
    {
        try
        {
            return XDocument.Load(path);
        }
        catch (XmlException exception)
        {
            throw new QualityInputException($"Cannot read Cobertura report {path}: {exception.Message}", exception);
        }
    }

    /// <summary>
    /// Adds the lines of one class element. Lines of nested classes share their source file,
    /// so the merge is keyed by file name rather than class name.
    /// </summary>
    /// <param name="report">The merged report.</param>
    /// <param name="package">The package name.</param>
    /// <param name="classElement">The class element.</param>
    /// <param name="path">The report path, for error messages.</param>
    private static void AddClassLines(CoverageReport report, string package, XElement classElement, string path)
    {
        string file = RequiredAttribute(classElement, "filename", path);
        foreach (XElement line in classElement.Elements("lines").Elements("line"))
        {
            report.AddLine(package, file, IntegerAttribute(line, "number", path), IntegerAttribute(line, "hits", path));
        }
    }

    /// <summary>
    /// Builds the package element of a library.
    /// </summary>
    /// <param name="report">The merged report.</param>
    /// <param name="package">The package name.</param>
    /// <returns>The package element with its line rate and classes.</returns>
    private static XElement PackageElement(CoverageReport report, string package)
    {
        var classes = report.FilesOf(package).Select(file => new XElement(
            "class",
            new XAttribute("name", file.Key),
            new XAttribute("filename", file.Key),
            new XAttribute("line-rate", Rate(file.Value.Values.Count(hits => hits > 0), file.Value.Count)),
            new XElement("lines", file.Value.Select(LineElement))));
        (int covered, int total) = report.LineCounts(package);
        return new XElement(
            "package",
            new XAttribute("name", package),
            new XAttribute("line-rate", Rate(covered, total)),
            new XElement("classes", classes));
    }

    /// <summary>
    /// Builds a line element.
    /// </summary>
    /// <param name="line">The line number and its hits.</param>
    /// <returns>The line element.</returns>
    private static XElement LineElement(KeyValuePair<int, int> line)
    {
        return new XElement(
            "line",
            new XAttribute("number", line.Key),
            new XAttribute("hits", line.Value),
            new XAttribute("branch", "False"));
    }

    /// <summary>
    /// Formats a coverage ratio between 0 and 1 the way Cobertura writes it.
    /// </summary>
    /// <param name="covered">The covered line count.</param>
    /// <param name="total">The instrumented line count.</param>
    /// <returns>The invariant-culture ratio.</returns>
    private static string Rate(int covered, int total)
    {
        double rate = total == 0 ? 1.0 : (double)covered / total;
        return rate.ToString("R", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Reads an attribute that the format requires.
    /// </summary>
    /// <param name="element">The element.</param>
    /// <param name="name">The attribute name.</param>
    /// <param name="path">The report path, for error messages.</param>
    /// <returns>The attribute value.</returns>
    private static string RequiredAttribute(XElement element, string name, string path)
    {
        return element.Attribute(name)?.Value
            ?? throw new QualityInputException($"Cobertura report {path}: <{element.Name}> has no {name} attribute.");
    }

    /// <summary>
    /// Reads an integer attribute that the format requires.
    /// </summary>
    /// <param name="element">The element.</param>
    /// <param name="name">The attribute name.</param>
    /// <param name="path">The report path, for error messages.</param>
    /// <returns>The attribute value.</returns>
    private static int IntegerAttribute(XElement element, string name, string path)
    {
        string text = RequiredAttribute(element, name, path);
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value
            : throw new QualityInputException($"Cobertura report {path}: {name}=\"{text}\" is not an integer.");
    }
}
