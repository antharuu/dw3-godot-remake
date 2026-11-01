using System.Text.Json;
using Godot;

namespace Dw3Production.Presentation;

/// <summary>
/// Loads the decoded texture pages of a battle model. Each page is one consumer-selected PS1
/// tpage/CLUT combination whose 256x256 texels the extractor has already decoded to a PNG beside
/// the model JSON.
/// </summary>
internal static class BattleTexturePages
{
    private const int PageSize = 256;

    /// <summary>Loads every page of the <c>textures</c> array.</summary>
    /// <param name="textures">The <c>textures</c> array; must not be empty.</param>
    /// <param name="directory">Directory holding the page images.</param>
    /// <returns>The pages by texture key, in document order.</returns>
    /// <exception cref="InvalidOperationException">The array is empty, a kind is unsupported, an
    /// image is missing, unreadable or not 256x256, or a key repeats.</exception>
    public static Dictionary<int, BattleFaceTexture> Load(JsonElement textures, string directory)
    {
        if (textures.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("battle model has no textures");
        }

        var result = new Dictionary<int, BattleFaceTexture>();
        foreach (var texture in textures.EnumerateArray())
        {
            var key = texture.GetProperty("key").GetInt32();
            if (!result.TryAdd(key, LoadPage(texture, directory)))
            {
                throw new InvalidOperationException($"duplicate battle model texture key {key}");
            }
        }

        return result;
    }

    private static BattleFaceTexture LoadPage(JsonElement texture, string directory)
    {
        if (texture.GetProperty("kind").GetString() != "ps1-tpage")
        {
            throw new InvalidOperationException("unsupported battle model texture kind");
        }

        var imagePath = Path.Combine(directory, texture.GetProperty("image").GetString()
            ?? throw new InvalidOperationException("battle model texture image missing"));
        if (!File.Exists(imagePath))
        {
            throw new InvalidOperationException($"battle model texture image missing: {imagePath}");
        }

        var image = Image.LoadFromFile(imagePath);
        if (image is null || image.IsEmpty() || image.GetWidth() != PageSize || image.GetHeight() != PageSize)
        {
            throw new InvalidOperationException($"battle model texture is not a readable 256x256 page: {imagePath}");
        }

        var source = (Image)image.Duplicate();
        var working = (Image)image.Duplicate();
        return new BattleFaceTexture(source, working, ImageTexture.CreateFromImage(working));
    }
}
