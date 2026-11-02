namespace Dw3Production.Diagnostics.Field;

/// <summary>
/// Maps the probe entry names chosen by <see cref="FieldDiagnosticProbeRunner"/> to the scripted
/// field probes. Names not listed here are probes the host still runs itself.
/// </summary>
internal static class FieldProbeCatalog
{
    /// <summary>Creates the field probe registered under an entry name.</summary>
    /// <param name="entryName">Entry name, such as <c>RunLocomotionProbe</c>.</param>
    /// <param name="host">Running game the probe will drive.</param>
    /// <returns>The probe, or <see langword="null"/> when the name is not a field probe of this catalog.</returns>
    public static FieldProbe? Create(string entryName, IFieldProbeHost host) => entryName switch
    {
        "RunLocomotionProbe" => new LocomotionProbe(host),
        "RunF3Probe" => new F3Probe(host),
        "RunF3PerformanceProbe" => new F3PerformanceProbe(host),
        "RunDepthProbe" => new DepthProbe(host),
        "RunWaterProbe" => new WaterProbe(host),
        "RunSortYProbe" => new SortYProbe(host),
        "RunCueProbe" => new CueProbe(host),
        "RunM1AcceptanceTest" => new M1AcceptanceProbe(host),
        "RunM2NavProbe" => new M2NavProbe(host),
        "RunConditionalProbe" => new ConditionalProbe(host),
        "RunGatedBridgeProbe" => new GatedBridgeProbe(host),
        "RunNpcProbe" => new NpcProbe(host),
        "RunM2VideoProbe" => new M2VideoProbe(host),
        "RunObjectProbe" => new ObjectProbe(host),
        "RunClimbProbe" => new ClimbProbe(host),
        "RunDropProbe" => new DropProbe(host),
        _ => null,
    };
}
