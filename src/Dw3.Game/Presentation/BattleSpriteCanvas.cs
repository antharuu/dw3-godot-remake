using Godot;

namespace Dw3Production.Presentation;

/// <summary>
/// The production drawing side of action sprites: one canvas child per started effect, under the HUD
/// for the models layer and over it for the front layer, both clipped to the source screen, plus the
/// additive white-flash quad over the whole viewport.
/// </summary>
internal sealed class BattleSpriteCanvas : IBattleSpriteSurface, IDisposable
{
    private readonly Camera3D _camera;
    private readonly Node2D _models;
    private readonly Node2D _front;
    private readonly Control _modelsFrame;
    private readonly Control _frontFrame;
    private readonly ColorRect _flash;
    private readonly BattleSpriteTextureCache _textures = new();
    private readonly BattleSpriteMaterials _materials = new();
    private readonly Dictionary<BattleSpriteEffectInstance, BattleSpriteEffectNode> _nodes = [];

    /// <summary>Initializes a new instance of the <see cref="BattleSpriteCanvas"/> class.</summary>
    /// <param name="layer">Battle canvas layer.</param>
    /// <param name="hud">HUD node, the source menus layer: models-layer sprites go under it, front sprites over it.</param>
    /// <param name="camera">Battle camera projecting world anchors.</param>
    public BattleSpriteCanvas(CanvasLayer layer, Node hud, Camera3D camera)
    {
        _camera = camera;
        _models = new Node2D { Name = "BattleSpriteModelsLayer", TextureFilter = CanvasItem.TextureFilterEnum.Nearest };
        _front = new Node2D { Name = "BattleSpriteFrontLayer", TextureFilter = CanvasItem.TextureFilterEnum.Nearest };
        _modelsFrame = SourceFrame("BattleSpriteModelsFrame", _models);
        _frontFrame = SourceFrame("BattleSpriteFrontFrame", _front);
        _flash = new ColorRect
        {
            Name = "BattleWhiteFlash",
            Color = Colors.Black,
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add },
        };
        _flash.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(_modelsFrame);
        layer.MoveChild(_modelsFrame, hud.GetIndex());
        layer.AddChild(_frontFrame);
        layer.MoveChild(_frontFrame, hud.GetIndex() + 1);
        layer.AddChild(_flash);
        layer.MoveChild(_flash, _frontFrame.GetIndex() + 1);
    }

    /// <summary>Gets the live effect children, for lifecycle diagnostics.</summary>
    public IReadOnlyCollection<BattleSpriteEffectNode> Nodes => _nodes.Values;

    /// <summary>Gets the white-flash quad.</summary>
    public ColorRect Flash => _flash;

    /// <summary>Gets the number of distinct part layers loaded.</summary>
    public int LoadedLayers => _textures.Count;

    /// <summary>Shows or hides the sprite layers and the flash separately for a paired offscreen capture, without advancing them.</summary>
    /// <param name="sprites">Whether the sprite layers draw.</param>
    /// <param name="flash">Whether the white flash draws.</param>
    public void SetVisibleForProbe(bool sprites, bool flash)
    {
        _models.Visible = sprites;
        _front.Visible = sprites;
        _flash.SelfModulate = flash ? Colors.White : Colors.Transparent;
    }

    /// <inheritdoc/>
    public void Attach(BattleSpriteEffectInstance instance)
    {
        var node = new BattleSpriteEffectNode(instance, _textures, _materials);
        (instance.Layer == BattleSpriteLayer.Front ? _front : _models).AddChild(node);
        _nodes.Add(instance, node);
    }

    /// <inheritdoc/>
    public void Detach(BattleSpriteEffectInstance instance)
    {
        if (_nodes.Remove(instance, out BattleSpriteEffectNode? node))
        {
            node.Visible = false;
            node.QueueFree();
        }
    }

    /// <inheritdoc/>
    public void Present(IReadOnlyList<BattleSpriteEffectInstance> active, int flashLevel)
    {
        var placed = active.Where(_nodes.ContainsKey).Select(instance => (Instance: instance, Origin: Project(instance.Anchor, instance.Layer, out float depth), Depth: depth)).ToArray();
        foreach (var item in placed)
        {
            _nodes[item.Instance].Render(item.Origin, BattleHudLayout.Scale);
        }

        Order(placed.Where(item => item.Instance.Layer == BattleSpriteLayer.Models).Select(item => (item.Instance, item.Depth)));
        Order(placed.Where(item => item.Instance.Layer == BattleSpriteLayer.Front).Select(item => (item.Instance, item.Depth)));
        float level = flashLevel / (float)BattleWhiteFlash.FullLevel;
        _flash.Color = new Color(level, level, level);
        _flash.Visible = flashLevel > 0;
    }

    /// <summary>Removes every child and releases the shared textures and materials.</summary>
    public void Dispose()
    {
        foreach (BattleSpriteEffectInstance instance in _nodes.Keys.ToArray())
        {
            Detach(instance);
        }

        _modelsFrame.QueueFree();
        _frontFrame.QueueFree();
        _flash.QueueFree();
        _textures.Dispose();
        _materials.Dispose();
    }

    /// <summary>
    /// Wraps a sprite layer in a control clipped to the source 320x240 screen at the 3x layout, so a sprite
    /// outside the original screen stays invisible like on the console, while canvas coordinates stay unchanged.
    /// </summary>
    /// <param name="name">Frame node name.</param>
    /// <param name="content">Sprite layer root.</param>
    /// <returns>The clipping frame.</returns>
    private static Control SourceFrame(string name, Node2D content)
    {
        (int width, int height) = BattleHudLayout.ToViewportSize(BattleHudLayout.SourceWidth, BattleHudLayout.SourceHeight);
        var frame = new Control
        {
            Name = name,
            ClipContents = true,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Position = new Vector2(BattleHudLayout.OffsetX, BattleHudLayout.OffsetY),
            Size = new Vector2(width, height),
        };
        content.Position = new Vector2(-BattleHudLayout.OffsetX, -BattleHudLayout.OffsetY);
        frame.AddChild(content);
        return frame;
    }

    /// <summary>
    /// Orders children like the source ordering table: the farthest entry first, and within one entry the
    /// effect created last first, because each new primitive is linked ahead of the earlier ones.
    /// </summary>
    /// <param name="items">Effects of one layer with their ordering depth.</param>
    private void Order(IEnumerable<(BattleSpriteEffectInstance Instance, float Depth)> items)
    {
        int index = 0;
        foreach (var item in items.OrderByDescending(item => item.Depth).ThenByDescending(item => item.Instance.Serial))
        {
            BattleSpriteEffectNode node = _nodes[item.Instance];
            node.GetParent().MoveChild(node, index++);
        }
    }

    /// <summary>
    /// Projects an anchor to the canvas: world points through the battle camera, screen points through the
    /// 3x layout from their layer's drawing offset (the models layer is centred at (160, 120), the front layer is not).
    /// </summary>
    /// <param name="anchor">Script position.</param>
    /// <param name="layer">Source layer of the effect.</param>
    /// <param name="depth">Ordering depth: view distance, minus infinity at the front marker, infinity at the back marker.</param>
    /// <returns>Canvas pivot.</returns>
    private Vector2 Project(BattleSpriteAnchor anchor, BattleSpriteLayer layer, out float depth)
    {
        if (anchor.IsScreen)
        {
            depth = anchor.Z == BattleSpriteAnchor.FrontScreenMarker ? float.NegativeInfinity : float.PositiveInfinity;
            int offsetX = layer == BattleSpriteLayer.Models ? BattleHudLayout.SourceWidth / 2 : 0;
            int offsetY = layer == BattleSpriteLayer.Models ? BattleHudLayout.SourceHeight / 2 : 0;
            (int x, int y) = BattleHudLayout.ToViewport(anchor.X + offsetX, anchor.Y + offsetY);
            return new Vector2(x, y);
        }

        Vector3 world = BattleModel.Ps1Position(new Vector3(anchor.X, anchor.Y, anchor.Z));
        depth = -(_camera.GlobalTransform.AffineInverse() * world).Z;
        return _camera.UnprojectPosition(world);
    }
}
