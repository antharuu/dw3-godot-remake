using Godot;

namespace Dw3Production.Presentation;

/// <summary>Loads each prepared part layer once per battle renderer, from the pack only.</summary>
internal sealed class BattleSpriteTextureCache : IDisposable
{
    private readonly Dictionary<string, ImageTexture> _textures = new(StringComparer.Ordinal);

    /// <summary>Gets the number of distinct layers loaded.</summary>
    public int Count => _textures.Count;

    /// <summary>Returns the texture of one prepared layer.</summary>
    /// <param name="path">Absolute validated layer path.</param>
    /// <returns>The nearest-filtered texture.</returns>
    /// <exception cref="FormatException">The layer cannot be decoded.</exception>
    public ImageTexture Get(string path)
    {
        if (_textures.TryGetValue(path, out ImageTexture? texture))
        {
            return texture;
        }

        using Image image = Image.LoadFromFile(path);
        if (image is null || image.IsEmpty())
        {
            throw new FormatException($"Sprite layer cannot be decoded: {path}");
        }

        texture = ImageTexture.CreateFromImage(image);
        _textures.Add(path, texture);
        return texture;
    }

    /// <summary>Releases every loaded texture.</summary>
    public void Dispose()
    {
        foreach (ImageTexture texture in _textures.Values)
        {
            texture.Dispose();
        }

        _textures.Clear();
    }
}
