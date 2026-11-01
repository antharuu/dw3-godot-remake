using Dw3Production.Conversion;
using Godot;

namespace Dw3Production.Presentation;

/// <summary>Manages the on-screen texture cache for field sprites, decoded once from PNG and
/// reused across zone loads. Also provides the soft shadow texture.</summary>
internal sealed class FieldTextureCache : IDisposable
{
    private readonly Dictionary<string, Texture2D> _textures = new(StringComparer.Ordinal);
    private ImageTexture? _shadowTexture;

    /// <summary>Gets the number of images loaded from disk since this instance was created.</summary>
    public int ImageDecodes { get; private set; }

    /// <summary>Gets the number of Godot textures uploaded since this instance was created.</summary>
    public int TextureUploads { get; private set; }

    /// <summary>Releases all cached Godot textures.</summary>
    public void Dispose()
    {
        foreach (var texture in _textures.Values)
        {
            texture.Dispose();
        }

        _shadowTexture?.Dispose();
        _textures.Clear();
        _shadowTexture = null;
    }

    /// <summary>Returns the texture for the given pack-relative path, loading and caching it on first access.</summary>
    /// <param name="path">Pack-relative path to a PNG image.</param>
    /// <returns>The cached Godot texture for <paramref name="path"/>.</returns>
    /// <exception cref="CacheResourceException">Thrown when the image file is missing or unreadable.</exception>
    public Texture2D TextureFor(string path)
    {
        if (_textures.TryGetValue(path, out var texture))
        {
            return texture;
        }

        var image = Image.LoadFromFile(path);
        if (image is null || image.IsEmpty())
        {
            throw new CacheResourceException("CACHE_RESOURCE_UNAVAILABLE", $"image unreadable: {path}");
        }

        ImageDecodes++;
        texture = ImageTexture.CreateFromImage(image);
        TextureUploads++;
        _textures.Add(path, texture);
        return texture;
    }

    /// <summary>Returns the shared soft-shadow texture, building it lazily on first access.</summary>
    /// <returns>A 32×16 radial-gradient alpha texture for actor shadows.</returns>
    public ImageTexture ShadowTexture()
    {
        if (_shadowTexture is not null)
        {
            return _shadowTexture;
        }

        // The source shadow is a 32x16 sprite drawn under the actor (drawActor sprts:
        // w=0x20, h=0x10 at pos.x-16, pos.y+climbHeight-8). The full field-image shadow
        // texture is not yet decoded, so a soft dark ellipse of the same footprint stands
        // in and stays at the actor's foot during a climb. Presentation-only.
        _shadowTexture = BuildShadowTexture();
        return _shadowTexture;
    }

    private static ImageTexture BuildShadowTexture()
    {
        var image = Image.CreateEmpty(32, 16, false, Image.Format.Rgba8);
        for (var y = 0; y < 16; y++)
        {
            for (var x = 0; x < 32; x++)
            {
                var dx = (x - 15.5f) / 16f;
                var dy = (y - 7.5f) / 8f;
                var distance = dx * dx + dy * dy;
                var alpha = distance >= 1f ? 0f : 0.35f * (1f - distance);
                image.SetPixel(x, y, new Color(0f, 0f, 0f, alpha));
            }
        }

        return ImageTexture.CreateFromImage(image);
    }
}
