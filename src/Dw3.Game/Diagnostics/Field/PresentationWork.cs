using Dw3Production.Presentation;

namespace Dw3Production.Diagnostics.Field;

/// <summary>
/// Renderer work counters: PNG decodes, texture uploads, F3 overlay geometry rebuilds and F3 HUD
/// text refreshes. A steady F3 overlay must decode and upload nothing and rebuild at most once.
/// </summary>
/// <param name="ImageDecodes">PNG images decoded.</param>
/// <param name="TextureUploads">Textures uploaded to the GPU.</param>
/// <param name="GeometryBuilds">F3 overlay geometry rebuilds.</param>
/// <param name="HudRefreshes">F3 HUD text refreshes.</param>
internal readonly record struct PresentationWork(int ImageDecodes, int TextureUploads, int GeometryBuilds, int HudRefreshes)
{
    /// <summary>Reads the cumulative counters of a renderer.</summary>
    /// <param name="renderer">Field renderer.</param>
    /// <returns>The counters since the renderer was created.</returns>
    public static PresentationWork Capture(FieldRenderer renderer) =>
        new(renderer.ImageDecodes, renderer.TextureUploads, renderer.OverlayGeometryBuilds, renderer.OverlayHudRefreshes);

    /// <summary>Returns the work done between an earlier capture and this one.</summary>
    /// <param name="baseline">Earlier capture of the same renderer.</param>
    /// <returns>The per-counter difference.</returns>
    public PresentationWork Since(PresentationWork baseline) =>
        new(ImageDecodes - baseline.ImageDecodes, TextureUploads - baseline.TextureUploads, GeometryBuilds - baseline.GeometryBuilds, HudRefreshes - baseline.HudRefreshes);
}
