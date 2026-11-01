namespace Dw3.Extractor;

/// <summary>Application entry point.</summary>
public static class Program
{
    /// <summary>Runs the extractor CLI and returns a POSIX exit code.</summary>
    /// <param name="arguments">Command-line arguments as passed by the OS.</param>
    /// <returns>POSIX exit code: 0 on success, 1 on extraction error, 2 on usage error.</returns>
    public static int Main(string[] arguments) => Cli.Run(arguments, Console.Out, Console.Error);
}
