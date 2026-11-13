using Dw3Production.Conversion;
using Godot;

namespace Dw3Production.Presentation;

/// <summary>
/// The real canvas child of one started sprite effect. Each placed part pass is a rendering-server canvas
/// item owned by this node, drawn in source order, so the per-part ABR passes keep their stacking. Every
/// pass sits under its own clipping item: the source screen, or the viewport while the effect shows a
/// widened full-screen backdrop.
/// </summary>
internal sealed partial class BattleSpriteEffectNode : Node2D
{
    /// <summary>The palette provider of parts whose CLUT wraps into VRAM the game never writes (fully transparent).</summary>
    public const string ClearedVramPalette = "cleared-vram";

    private readonly BattleSpriteEffectInstance _instance;
    private readonly BattleSpriteTextureCache _textures;
    private readonly BattleSpriteMaterials _materials;
    private readonly List<(Rid Clip, Rid Item)> _items = [];
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

    /// <summary>Gets the number of drawn samples widened as full-screen backdrops by the latest render.</summary>
    public int WidenedSamples { get; private set; }

    /// <summary>Gets the number of parts drawn by the latest render.</summary>
    public int DrawnParts { get; private set; }

    /// <summary>Gets the number of parts of the latest render that read the never-written VRAM palette, which the
    /// source draws fully transparent.</summary>
    public int ClearedParts { get; private set; }

    /// <summary>Gets a value indicating whether every drawn part of the latest render reads the never-written
    /// VRAM palette, so the source draws nothing visible for it.</summary>
    public bool OnlyClearedPalettes => DrawnParts > 0 && ClearedParts == DrawnParts;

    /// <summary>Redraws the current samples around the projected pivot.</summary>
    /// <param name="origin">Canvas position of the effect anchor.</param>
    /// <param name="pixelScale">Canvas pixels per source screen pixel.</param>
    public void Render(Vector2 origin, float pixelScale)
    {
        _used = 0;
        int parts = 0;
        int cleared = 0;
        var samples = _instance.DrawnSamples().Select(item => Place(origin, pixelScale, item.Sample)).ToArray();
        WidenedSamples = samples.Count(sample => sample.Backdrop);
        Rect2 clip = WidenedSamples > 0 ? BattleSpriteCoverage.ViewportClip : BattleSpriteCoverage.SourceClip;
        foreach ((Transform2D transform, BattleSpriteFrame frame, bool backdrop) in samples)
        {
            foreach (BattleSpritePart part in frame.Parts)
            {
                DrawPart(backdrop ? BattleSpriteCoverage.Widen * transform : transform, clip, part, frame);
                parts++;
                cleared += part.PaletteSource == ClearedVramPalette ? 1 : 0;
            }
        }

        DrawnParts = parts;
        ClearedParts = cleared;
        for (int i = _used; i < _items.Count; i++)
        {
            RenderingServer.CanvasItemClear(_items[i].Item);
        }
    }

    /// <inheritdoc/>
    public override void _Notification(int what)
    {
        if (what == NotificationPredelete)
        {
            foreach ((Rid clip, Rid item) in _items)
            {
                RenderingServer.FreeRid(item);
                RenderingServer.FreeRid(clip);
            }

            _items.Clear();
        }
    }

    /// <summary>
    /// Places one sample around the projected pivot and tells whether it is a full-screen backdrop. While an
    /// effect shows a backdrop, all its samples are clipped to the viewport rather than the source screen.
    /// </summary>
    /// <param name="origin">Canvas position of the effect anchor.</param>
    /// <param name="pixelScale">Canvas pixels per source screen pixel.</param>
    /// <param name="sample">Drawn sample.</param>
    /// <returns>Its transform, frame and backdrop flag.</returns>
    private (Transform2D Transform, BattleSpriteFrame Frame, bool Backdrop) Place(Vector2 origin, float pixelScale, BattleSpriteSample sample)
    {
        var pivot = origin + (new Vector2(sample.X, sample.Y) * pixelScale);
        Transform2D transform = new Transform2D(0f, pivot) * Transform2D.Identity.Scaled(Vector2.One * pixelScale) * sample.LinearTransform();
        BattleSpriteFrame frame = _instance.Sheet.FrameOf(sample);
        return (transform, frame, _instance.Anchor.IsScreen && BattleSpriteCoverage.SpansSourceScreen(frame, BattleSpriteCoverage.ToSourceX(pivot.X)));
    }

    /// <summary>Draws the source passes of one placed part.</summary>
    /// <param name="transform">Sprite transform around its pivot.</param>
    /// <param name="clip">Viewport rectangle the passes are clipped to.</param>
    /// <param name="part">Placed layer.</param>
    /// <param name="frame">Owning frame and its blend mode.</param>
    private void DrawPart(Transform2D transform, Rect2 clip, BattleSpritePart part, BattleSpriteFrame frame)
    {
        ImageTexture texture = _textures.Get(part.Path);
        var rect = new Rect2(part.Dx, part.Dy, part.Width, part.Height);
        if (!frame.IsSemiTransparent)
        {
            DrawPass(transform, clip, (rect, texture), _materials.OpaqueFrame);
            return;
        }

        DrawPass(transform, clip, (rect, texture), _materials.NonStpPass);
        DrawPass(transform, clip, (rect, texture), _materials.StpPass(frame.Abr));
    }

    /// <summary>Fills the next pooled canvas item with one textured quad under its clipping item.</summary>
    /// <param name="transform">Sprite transform.</param>
    /// <param name="clip">Viewport rectangle the pass is clipped to.</param>
    /// <param name="quad">Placement rectangle in source pixels relative to the pivot, and the layer texture.</param>
    /// <param name="material">Pass material.</param>
    private void DrawPass(Transform2D transform, Rect2 clip, (Rect2 Rect, Texture2D Texture) quad, Material material)
    {
        if (_used == _items.Count)
        {
            Rid holder = RenderingServer.CanvasItemCreate();
            RenderingServer.CanvasItemSetParent(holder, GetCanvasItem());
            RenderingServer.CanvasItemSetClip(holder, true);
            Rid created = RenderingServer.CanvasItemCreate();
            RenderingServer.CanvasItemSetParent(created, holder);
            RenderingServer.CanvasItemSetDefaultTextureFilter(created, RenderingServer.CanvasItemTextureFilter.Nearest);
            _items.Add((holder, created));
        }

        (Rid owner, Rid item) = _items[_used++];
        RenderingServer.CanvasItemSetCustomRect(owner, true, clip);
        RenderingServer.CanvasItemClear(item);
        RenderingServer.CanvasItemSetMaterial(item, material.GetRid());
        RenderingServer.CanvasItemSetTransform(item, transform);
        RenderingServer.CanvasItemAddTextureRect(item, quad.Rect, quad.Texture.GetRid());
    }
}
