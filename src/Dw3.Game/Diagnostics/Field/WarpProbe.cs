using Dw3Production.Logging;
using static Dw3Production.Diagnostics.Field.ProbeRequirement;
using NumericsVector2 = System.Numerics.Vector2;

namespace Dw3Production.Diagnostics.Field;

/// <summary>
/// Crosses the S200 to S235 exit, back, and again, checking each arrival against the decoded
/// warp targets and that the warp commits exactly once per crossing.
/// </summary>
internal sealed class WarpProbe : FieldProbe
{
    /// <summary>Initializes a new instance of the <see cref="WarpProbe"/> class.</summary>
    /// <param name="host">Running game.</param>
    public WarpProbe(IFieldProbeHost host)
        : base(host)
    {
    }

    /// <inheritdoc/>
    protected override string FailureCode => "WARP_PROBE";

    /// <inheritdoc/>
    protected override async Task ExecuteAsync()
    {
        var state = Host.State;
        WarpEdgeDriver.Run(Host, "S200", 4, 3, "s200-to-s235");
        Require(state.ZoneId == "S235" && state.Position == new NumericsVector2(278, 218) && state.Facing == 3, "S200 manifest arrival mismatch");
        await CaptureAsync("warp-s235-entry.png");
        WarpEdgeDriver.Run(Host, "S235", 1, 7, "s235-to-s200");
        Require(state.ZoneId == "S200" && state.Position == new NumericsVector2(672, 378) && state.Facing == 7, "S235 manifest arrival mismatch");
        WarpEdgeDriver.Run(Host, "S200", 4, 3, "s200-to-s235-reentry");
        Require(state.ZoneId == "S235" && Host.Warp.CommitCount == 3, "re-entry did not commit exactly once per edge");
        await CaptureAsync("warp-s235-reentry.png");
        QuietLog.Info($"warp probe PASS commits={Host.Warp.CommitCount} zone={state.ZoneId} state={Host.Warp.State}");
    }

    /// <summary>Writes the next drawn frame without presenting again, so it shows the arrival as the warp left it.</summary>
    /// <param name="fileName">PNG file name.</param>
    /// <returns>A task that completes once the PNG is written.</returns>
    private async Task CaptureAsync(string fileName)
    {
        await Host.NextFrameDrawn();
        var capturePath = Path.Combine(Host.PrepareCaptureDirectory(), fileName);
        ViewportCapture.Save(Host.CaptureViewport(), capturePath, $"warp capture failed={capturePath}");
        QuietLog.Info($"warp capture={capturePath}");
    }
}
