namespace Dw3.QualityRatchet;

/// <summary>
/// Entry point of the quality ratchet command line.
/// </summary>
internal static class Program
{
    /// <summary>
    /// Runs the command named by the first argument against the repository containing the working directory.
    /// </summary>
    /// <param name="args">The command and its options.</param>
    /// <returns>0 when the ratchet holds, 1 when it is broken, 2 when the input is invalid.</returns>
    private static int Main(string[] args)
    {
        return new RatchetCommand(Console.Out, Console.Error).Run(args, Directory.GetCurrentDirectory());
    }
}
