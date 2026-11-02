using Dw3Production.Logging;
using Godot;

namespace Dw3Production.Diagnostics.Field;

/// <summary>
/// Writes <c>s200-spawn.png</c> once the first field frame is drawn, as evidence that an ordinary
/// session reached the field. With <c>DW3_CAPTURE_AND_QUIT</c> the session then ends with code 0;
/// otherwise play continues.
/// </summary>
internal static class SpawnCapture
{
    /// <summary>Captures the first drawn frame.</summary>
    /// <param name="host">Running game.</param>
    /// <param name="quitAfterCapture">Ends the session with exit code 0 after a successful capture.</param>
    /// <returns>A task that completes once the capture is written or its failure reported.</returns>
    public static async Task RunAsync(IFieldProbeHost host, bool quitAfterCapture)
    {
        await host.NextFrameDrawn();
        var capturePath = Path.Combine(host.PrepareCaptureDirectory(), "s200-spawn.png");
        var status = host.CaptureViewport().SavePng(capturePath);
        if (status != Error.Ok)
        {
            GD.PushError($"CACHE_RESOURCE_UNAVAILABLE: capture write failed: {capturePath} ({status})");
            host.Quit(3);
            return;
        }

        QuietLog.Info($"capture={capturePath}");
        if (quitAfterCapture)
        {
            host.Quit(0);
        }
    }
}
