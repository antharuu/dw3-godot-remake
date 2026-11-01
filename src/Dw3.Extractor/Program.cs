namespace Dw3.Extractor;

/// <summary>Application entry point.</summary>
public static class Program
{
    /// <summary>Runs the extractor CLI and returns a POSIX exit code.</summary>
    public static int Main(string[] arguments) => Cli.Run(arguments, Console.Out, Console.Error);
}
