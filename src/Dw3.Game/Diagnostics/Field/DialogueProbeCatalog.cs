namespace Dw3Production.Diagnostics.Field;

/// <summary>
/// Maps the dialogue entry name chosen by <see cref="DialogueDiagnosticProbeRunner"/> to its probe.
/// The connection-icon probe of the same runner is a plain field probe of <see cref="FieldProbeCatalog"/>.
/// </summary>
internal static class DialogueProbeCatalog
{
    /// <summary>Creates the dialogue probe registered under an entry name.</summary>
    /// <param name="entryName">Entry name, <c>RunDialogueProbe</c>.</param>
    /// <param name="host">Running game the probe will drive.</param>
    /// <returns>The probe, or <see langword="null"/> when the name is not a dialogue probe.</returns>
    public static FieldProbe? Create(string entryName, IDialogueProbeHost host) => entryName switch
    {
        "RunDialogueProbe" => new DialogueProbe(host),
        _ => null,
    };
}
