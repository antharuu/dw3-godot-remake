using Dw3Production.Conversion;
using Godot;

namespace Dw3Production.Presentation;

/// <summary>
/// The report's decoded sprite frames, split by PlayStation drawing mode. A texel whose semi-transparency bit is set
/// (decoded with alpha 128) is drawn by the original report additively, background plus texel, while every other
/// texel is opaque. The opaque texels and the additive texels are kept in separate textures so each is drawn by a
/// canvas item with the matching blend.
/// </summary>
internal sealed class FightReportSpriteSheet
{
    /// <summary>Decoded alpha of a texel whose semi-transparency bit is set.</summary>
    private const byte SemiTransparentAlpha = 128;

    private readonly Dictionary<int, ImageTexture[]> _opaque = [];
    private readonly Dictionary<int, ImageTexture?[]> _additive = [];
    private readonly Dictionary<int, Vector2> _offsets = [];

    /// <summary>Loads every frame's colour rows and splits their texels by drawing mode.</summary>
    /// <param name="sprites">Decoded report sprites.</param>
    /// <exception cref="CacheResourceException">A frame image is unreadable.</exception>
    public void Load(BattleReportSprites sprites)
    {
        ArgumentNullException.ThrowIfNull(sprites);
        _opaque.Clear();
        _additive.Clear();
        _offsets.Clear();
        foreach (var (id, frame) in sprites.Frames)
        {
            var images = frame.ClutRows.Select(Image.LoadFromFile).ToArray();
            if (images.Any(image => image is null || image.IsEmpty()))
            {
                throw new CacheResourceException("CACHE_RESOURCE_UNAVAILABLE", $"battle report frame unreadable frame={id}");
            }

            var split = images.Select(Split).ToArray();
            _opaque[id] = [.. split.Select(part => ImageTexture.CreateFromImage(part.Opaque))];
            _additive[id] = [.. split.Select(part => part.Additive is null ? null : ImageTexture.CreateFromImage(part.Additive))];
            _offsets[id] = new Vector2(frame.OffsetX, frame.OffsetY);
        }
    }

    /// <summary>Draws the opaque texels of one frame colour row.</summary>
    /// <param name="target">Canvas item drawing with the normal blend.</param>
    /// <param name="frame">Frame identifier.</param>
    /// <param name="position">Destination before the frame's decoded offset.</param>
    /// <param name="row">Colour row.</param>
    public void DrawOpaque(CanvasItem target, int frame, Vector2 position, int row = 0)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (_opaque.TryGetValue(frame, out var rows))
        {
            target.DrawTexture(rows[Math.Clamp(row, 0, rows.Length - 1)], position + _offsets[frame]);
        }
    }

    /// <summary>Draws the semi-transparent texels of one frame colour row, if it has any.</summary>
    /// <param name="target">Canvas item drawing with the additive blend.</param>
    /// <param name="frame">Frame identifier.</param>
    /// <param name="position">Destination before the frame's decoded offset.</param>
    /// <param name="row">Colour row.</param>
    public void DrawAdditive(CanvasItem target, int frame, Vector2 position, int row = 0)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (_additive.TryGetValue(frame, out var rows) && rows[Math.Clamp(row, 0, rows.Length - 1)] is { } texture)
        {
            target.DrawTexture(texture, position + _offsets[frame]);
        }
    }

    /// <summary>Splits one decoded colour row into its opaque texels and its full-strength semi-transparent texels.</summary>
    /// <param name="image">Decoded colour row.</param>
    /// <returns>The opaque image, and the additive image or null when no texel is semi-transparent.</returns>
    private static (Image Opaque, Image? Additive) Split(Image image)
    {
        image.Convert(Image.Format.Rgba8);
        byte[] data = image.GetData();
        byte[] opaque = (byte[])data.Clone();
        byte[] additive = (byte[])data.Clone();
        bool any = false;
        for (int alpha = 3; alpha < data.Length; alpha += 4)
        {
            bool semi = data[alpha] == SemiTransparentAlpha;
            any |= semi;
            opaque[alpha] = semi ? (byte)0 : data[alpha];
            additive[alpha] = semi ? byte.MaxValue : (byte)0;
        }

        int width = image.GetWidth();
        int height = image.GetHeight();
        Image opaqueImage = Image.CreateFromData(width, height, false, Image.Format.Rgba8, opaque);
        return (opaqueImage, any ? Image.CreateFromData(width, height, false, Image.Format.Rgba8, additive) : null);
    }
}
