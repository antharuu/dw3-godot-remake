using Godot;

namespace Dw3Production.Diagnostics.Field;

/// <summary>Renders the field without the F3 overlay and reads the drawn viewport back.</summary>
internal static class ViewportCapture
{
    /// <summary>Presents the current state with F3 off and returns the drawn frame.</summary>
    /// <param name="host">Running game.</param>
    /// <returns>The 1280x720 viewport image.</returns>
    public static async Task<Image> PresentAsync(IFieldProbeHost host)
    {
        host.Renderer.Present(host.State, host.CurrentZone, f3On: false);
        await host.NextFrameDrawn();
        return host.CaptureViewport();
    }

    /// <summary>Writes a capture to disk.</summary>
    /// <param name="image">Captured viewport.</param>
    /// <param name="path">PNG path.</param>
    /// <param name="failure">Failure message naming the capture.</param>
    /// <exception cref="InvalidOperationException">The PNG could not be written.</exception>
    public static void Save(Image image, string path, string failure) =>
        ProbeRequirement.Require(image.SavePng(path) == Error.Ok, failure);
}
