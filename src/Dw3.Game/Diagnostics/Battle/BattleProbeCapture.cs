using Dw3Production.Diagnostics.Field;
using Godot;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>Synchronizes battle presentation with framebuffer capture for headless diagnostics.</summary>
internal static class BattleProbeCapture
{
    /// <summary>Advances the real presentation path to settled lighting, then writes a PNG.</summary>
    /// <param name="host">Active battle and viewport.</param>
    /// <param name="captureRoot">Directory receiving the capture.</param>
    /// <param name="fileName">PNG file name.</param>
    /// <returns>The captured viewport image.</returns>
    /// <exception cref="InvalidOperationException">The battle view or PNG write is unavailable.</exception>
    public static async Task<Image> CaptureAsync(IBattleProbeHost host, string captureRoot, string fileName)
    {
        while (host.Renderer.LightFade < 1f)
        {
            host.Renderer.Present(host.BuildBattleView());
            await host.NextFrameDrawn();
        }

        host.Renderer.Present(host.BuildBattleView());
        await host.NextFrameDrawn();
        await host.NextFrameDrawn();
        var image = host.CaptureViewport();
        var path = Path.Combine(captureRoot, fileName);
        ProbeRequirement.Require(image.SavePng(path) == Error.Ok, $"battle capture failed={path}");
        return image;
    }

    /// <summary>Writes the next raw framebuffer without advancing battle presentation.</summary>
    /// <param name="host">Active battle and viewport.</param>
    /// <param name="captureRoot">Directory receiving the capture.</param>
    /// <param name="fileName">PNG file name.</param>
    /// <returns>The captured viewport image.</returns>
    /// <exception cref="InvalidOperationException">The PNG cannot be written.</exception>
    public static async Task<Image> CaptureRawAsync(IBattleProbeHost host, string captureRoot, string fileName)
    {
        await host.NextFrameDrawn();
        var image = host.CaptureViewport();
        var path = Path.Combine(captureRoot, fileName);
        ProbeRequirement.Require(image.SavePng(path) == Error.Ok, $"battle capture failed={path}");
        return image;
    }
}
