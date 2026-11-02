using Dw3Production.Conversion;
using Godot;

namespace Dw3Production.Presentation;

/// <summary>Uploads PNG images of the data pack as GPU textures.</summary>
internal static class PackTextures
{
    /// <summary>Loads a PNG file and creates a texture from it.</summary>
    /// <param name="path">Absolute PNG path inside the data pack.</param>
    /// <returns>The texture, with the image's own size and format.</returns>
    /// <exception cref="CacheResourceException">The file is missing, unreadable or empty.</exception>
    public static ImageTexture Load(string path)
    {
        var image = Image.LoadFromFile(path);
        if (image is null || image.IsEmpty())
        {
            throw new CacheResourceException("CACHE_RESOURCE_UNAVAILABLE", $"image unreadable: {path}");
        }

        return ImageTexture.CreateFromImage(image);
    }
}
