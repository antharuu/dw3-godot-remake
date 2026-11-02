using Godot;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>Loads optional private PSX reference frames for battle comparison probes.</summary>
internal static class PsxReferenceImages
{
    /// <summary>Loads a private reference image for optional battle comparisons.</summary>
    /// <param name="subPath">Forward-slash path relative to <c>DW3_PSX_REFERENCE_DIR</c>.</param>
    /// <returns>The decoded reference image.</returns>
    /// <exception cref="PsxReferenceUnavailableException">The root, file, or decoded image is unavailable.</exception>
    public static Image Load(string subPath)
    {
        var referenceDirectory = System.Environment.GetEnvironmentVariable("DW3_PSX_REFERENCE_DIR");
        if (referenceDirectory is null)
        {
            throw new PsxReferenceUnavailableException(
                "set DW3_PSX_REFERENCE_DIR to the root directory that contains battle/... frames");
        }

        var fullPath = Path.Combine(referenceDirectory, subPath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(fullPath))
        {
            throw new PsxReferenceUnavailableException($"file not found: {fullPath}");
        }

        var image = Image.LoadFromFile(fullPath);
        if (image is null || image.IsEmpty())
        {
            throw new PsxReferenceUnavailableException($"image unreadable: {fullPath}");
        }

        return image;
    }
}
