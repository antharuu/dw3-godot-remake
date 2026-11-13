using Dw3Production.Conversion;
using Godot;

namespace Dw3Production.Presentation;

/// <summary>
/// The real canvas child of one started sprite effect. Each placed part is a rendering-server canvas
/// item owned by this node, drawn in source order, so the per-part ABR passes keep their stacking.
/// </summary>
internal sealed partial class BattleSpriteEffectNode : Node2D
{
    private readonly BattleSpriteEffectInstance _instance;
    private readonly BattleSpriteTextureCache _textures;
    private readonly BattleSpriteMaterials _materials;
    private readonly List<Rid> _items = [];
    private int _used;

    /// <summary>Initializes a new instance of the <see cref="BattleSpriteEffectNode"/> class.</summary>
    /// <param name="instance">Started effect drawn by this node.</param>
    /// <param name="textures">Shared layer textures.</param>
    /// <param name="materials">Shared blend materials.</param>
    public BattleSpriteEffectNode(BattleSpriteEffectInstance instance, BattleSpriteTextureCache textures, BattleSpriteMaterials materials)
    {
        _instance = instance;
        _textures = textures;
        _materials = materials;
        Name = $"SpriteEffect{instance.Id}_{instance.Serial}";
        TextureFilter = TextureFilterEnum.Nearest;
    }

    /// <summary>Gets the effect drawn by this node.</summary>
    public BattleSpriteEffectInstance Instance => _instance;

    /// <summary>Gets the number of part passes drawn by the latest render.</summary>
    public int DrawnPasses => _used;

    /// <summary>Redraws the current samples around the projected pivot.</summary>
    /// <param name="origin">Canvas position of the effect anchor.</param>
    /// <param name="pixelScale">Canvas pixels per source screen pixel.</param>
    public void Render(Vector2 origin, float pixelScale)
    {
        _used = 0;
        foreach ((_, BattleSpriteSample sample) in _instance.DrawnSamples())
        {
            var pivot = origin + (new Vector2(sample.X, sample.Y) * pixelScale);
            Transform2D transform = new Transform2D(0f, pivot) * Transform2D.Identity.Scaled(Vector2.One * pixelScale) * sample.LinearTransform();
            BattleSpriteFrame frame = _instance.Sheet.FrameOf(sample);
            foreach (BattleSpritePart part in frame.Parts)
            {
                DrawPart(transform, part, frame);
            }
        }

        for (int i = _used; i < _items.Count; i++)
        {
            RenderingServer.CanvasItemClear(_items[i]);
        }
    }

    /// <inheritdoc/>
    public override void _Notification(int what)
    {
        if (what == NotificationPredelete)
        {
            foreach (Rid item in _items)
            {
                RenderingServer.FreeRid(item);
            }

            _items.Clear();
        }
    }

    /// <summary>Draws the source passes of one placed part.</summary>
    /// <param name="transform">Sprite transform around its pivot.</param>
    /// <param name="part">Placed layer.</param>
    /// <param name="frame">Owning frame and its blend mode.</param>
    private void DrawPart(Transform2D transform, BattleSpritePart part, BattleSpriteFrame frame)
    {
        ImageTexture texture = _textures.Get(part.Path);
        var rect = new Rect2(part.Dx, part.Dy, part.Width, part.Height);
        if (!frame.IsSemiTransparent)
        {
            DrawPass(transform, rect, texture, _materials.OpaqueFrame);
            return;
        }

        DrawPass(transform, rect, texture, _materials.NonStpPass);
        DrawPass(transform, rect, texture, _materials.StpPass(frame.Abr));
    }

    /// <summary>Fills the next pooled canvas item with one textured quad.</summary>
    /// <param name="transform">Sprite transform.</param>
    /// <param name="rect">Placement rectangle in source pixels relative to the pivot.</param>
    /// <param name="texture">Layer texture.</param>
    /// <param name="material">Pass material.</param>
    private void DrawPass(Transform2D transform, Rect2 rect, Texture2D texture, Material material)
    {
        if (_used == _items.Count)
        {
            Rid created = RenderingServer.CanvasItemCreate();
            RenderingServer.CanvasItemSetParent(created, GetCanvasItem());
            RenderingServer.CanvasItemSetDefaultTextureFilter(created, RenderingServer.CanvasItemTextureFilter.Nearest);
            _items.Add(created);
        }

        Rid item = _items[_used++];
        RenderingServer.CanvasItemClear(item);
        RenderingServer.CanvasItemSetMaterial(item, material.GetRid());
        RenderingServer.CanvasItemSetTransform(item, transform);
        RenderingServer.CanvasItemAddTextureRect(item, rect, texture.GetRid());
    }
}
