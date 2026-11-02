using Dw3Production.Logging;
using static Dw3Production.Diagnostics.Field.ProbeRequirement;

namespace Dw3Production.Diagnostics.Field;

/// <summary>
/// M2 multi-zone navigation: drives <see cref="CityLoopRoute"/> from the untouched S200 spawn with
/// native input only, then checks four commits, the final S200 arrival, F3 left off, and that the
/// S200 re-entry reused the cached zone (three decodes for three zones).
/// </summary>
internal sealed class M2NavProbe : FieldProbe
{
    /// <summary>Initializes a new instance of the <see cref="M2NavProbe"/> class.</summary>
    /// <param name="host">Running game.</param>
    public M2NavProbe(IFieldProbeHost host)
        : base(host)
    {
    }

    /// <inheritdoc/>
    protected override string FailureCode => "M2_NAV_PROBE";

    /// <inheritdoc/>
    protected override async Task ExecuteAsync()
    {
        var frameRoot = Path.Combine(Host.PrepareCaptureDirectory(), "m2-nav-frames");
        ProbeVideoEncoder.ResetFrameDirectory(frameRoot);
        Require(CityLoopRoute.StandsAtSpawn(Host), "M2 navigation must begin at the source S200 spawn without repositioning");

        await CityLoopRoute.DriveAsync(Host, new FrameSequenceRecorder(Host, frameRoot));

        var state = Host.State;
        Require(Host.Warp.CommitCount == CityLoopRoute.Commits, $"M2 navigation committed {Host.Warp.CommitCount} times instead of four");
        Require(CityLoopRoute.EndedAtFinalArrival(Host), "M2 navigation did not return to S200 via S235 slot1");
        Require(!Host.Input.F3On, "M2 navigation enabled F3");
        Require(Host.ZoneDecodeCount == 3, $"M2 navigation decoded {Host.ZoneDecodeCount} zones instead of three (S200/S210/S235): a re-entry must not re-decode");
        QuietLog.Info($"m2 nav PASS commits={Host.Warp.CommitCount} zones=S200,S210,S235 decodes={Host.ZoneDecodeCount} route=S200->S210->S200->S235->S200 final_zone={state.ZoneId} final_position=({state.Position.X:0},{state.Position.Y:0}) facing={state.Facing}");
    }
}
