using Xunit;

namespace Dw3.Extractor.Tests;

public sealed class CliTests
{
    [Fact]
    public void VersionOptionPrintsAssemblyVersionAndSucceeds()
    {
        using var output = new StringWriter();

        int exitCode = Cli.Run(["--version"], output, TextWriter.Null);

        Assert.Equal(0, exitCode);
        Assert.Equal("0.1.0" + Environment.NewLine, output.ToString());
    }

    [Fact]
    public void SchemaOptionReportsTheWriterSchemaWithoutOpeningDisc()
    {
        using var output = new StringWriter();

        int exitCode = Cli.Run(["--schema-version"], output, TextWriter.Null);

        Assert.Equal(0, exitCode);
        Assert.Equal(Dw3.Data.DataPackSchema.Version.ToString(System.Globalization.CultureInfo.InvariantCulture) + Environment.NewLine, output.ToString());
    }

    [Fact]
    public void ExtractUsageListsSupportedDataDomains()
    {
        using var error = new StringWriter();

        int exitCode = Cli.Run(["extract"], TextWriter.Null, error);

        Assert.Equal(2, exitCode);
        Assert.Contains("dialogue", error.ToString());
        Assert.Contains("battle", error.ToString());
        Assert.Contains("digivolution", error.ToString());
        Assert.Contains("battle-models", error.ToString());
    }

    [Fact]
    public void FullExtractionJsonProgressReportsUsageAsFinalJsonError()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = Cli.Run(["extract", "--progress", "jsonl"], output, error);

        Assert.Equal(2, exitCode);
        Assert.Empty(error.ToString());
        Assert.Equal("{\"event\":\"error\",\"code\":\"USAGE\",\"message\":\"Both --disc and --out are required.\"}" + Environment.NewLine, output.ToString());
    }

    [Fact]
    public void UnknownOptionPrintsAnActionableError()
    {
        using var error = new StringWriter();

        int exitCode = Cli.Run(["--unknown"], TextWriter.Null, error);

        Assert.Equal(2, exitCode);
        Assert.Contains("Unknown option: --unknown", error.ToString());
        Assert.Contains("--version", error.ToString());
    }
}
