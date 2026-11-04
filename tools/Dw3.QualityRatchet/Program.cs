namespace Dw3.QualityRatchet;

/// <summary>
/// Entry point of the coverage check command line.
/// </summary>
internal static class Program
{
    /// <summary>
    /// Runs the command named by the first argument against the repository containing the working directory.
    /// </summary>
    /// <param name="args">The command and its options.</param>
    /// <returns>0 when every library reaches the minimum, 1 when one does not, 2 when the input is invalid.</returns>
    private static int Main(string[] args)
    {
        return new RatchetCommand(Console.Out, Console.Error).Run(args, Directory.GetCurrentDirectory());
    }
}
