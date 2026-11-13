using Godot;

namespace Dw3Production.Presentation;

/// <summary>
/// The production drawing side of action sprites: one canvas child per started effect. A models-layer effect
/// is sorted into the fighters' ordering table by its source entry, under the HUD; a front-layer effect is
/// drawn over the HUD. The additive white-flash quad covers the whole viewport.
/// </summary>
internal sealed class BattleSpriteCanvas : IBattleSpriteSurface, IDisposable
{
    private readonly Camera3D _camera;
    private readonly BattleModelLayer _models;
    private readonly Node2D _front;
    private readonly ColorRect _flash;
    private readonly BattleSpriteTextureCache _textures = new();
    private readonly BattleSpriteMaterials _materials = new();
    private readonly Dictionary<BattleSpriteEffectInstance, BattleSpriteEffectNode> _nodes = [];
    private bool _spritesVisible = true;

    /// <summary>Initializes a new instance of the <see cref="BattleSpriteCanvas"/> class.</summary>
    /// <param name="layer">Battle canvas layer.</param>
    /// <param name="hud">HUD node, the source menus layer: front sprites go over it.</param>
    /// <param name="camera">Battle camera projecting world anchors.</param>
    /// <param name="models">The fighters' ordered polygon stream, which also orders the models-layer sprites.</param>
    public BattleSpriteCanvas(CanvasLayer layer, Node hud, Camera3D camera, BattleModelLayer models)
    {
        _camera = camera;
        _models = models;
        _front = new Node2D { Name = "BattleSpriteFrontLayer", TextureFilter = CanvasItem.TextureFilterEnum.Nearest };
        _flash = new ColorRect
        {
            Name = "BattleWhiteFlash",
            Color = Colors.Black,
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add },
        };
        _flash.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(_front);
        layer.MoveChild(_front, hud.GetIndex() + 1);
        layer.AddChild(_flash);
        layer.MoveChild(_flash, _front.GetIndex() + 1);
    }

    /// <summary>Gets the live effect children, for lifecycle diagnostics.</summary>
    public IReadOnlyCollection<BattleSpriteEffectNode> Nodes => _nodes.Values;

    /// <summary>Gets the white-flash quad.</summary>
    public ColorRect Flash => _flash;

    /// <summary>Gets the number of distinct part layers loaded.</summary>
    public int LoadedLayers => _textures.Count;

    /// <summary>Shows or hides every sprite and the flash separately for a paired offscreen capture, without advancing them.</summary>
    /// <param name="sprites">Whether the sprites draw.</param>
    /// <param name="flash">Whether the white flash draws.</param>
    public void SetVisibleForProbe(bool sprites, bool flash)
    {
        _spritesVisible = sprites;
        foreach (BattleSpriteEffectNode node in _nodes.Values)
        {
            node.Visible = sprites;
        }

        _flash.SelfModulate = flash ? Colors.White : Colors.Transparent;
    }

    /// <summary>Shows or hides one effect instance for a held per-instance control capture, without advancing it.</summary>
    /// <param name="instance">Live instance.</param>
    /// <param name="visible">Whether it draws.</param>
    /// <returns>Whether the instance has a live child.</returns>
    public bool SetInstanceVisibleForProbe(BattleSpriteEffectInstance instance, bool visible)
    {
        if (!_nodes.TryGetValue(instance, out BattleSpriteEffectNode? node))
        {
            return false;
        }

        node.Visible = visible && _spritesVisible;
        return true;
    }

    /// <inheritdoc/>
    public void Attach(BattleSpriteEffectInstance instance)
    {
        var node = new BattleSpriteEffectNode(instance, _textures, _materials) { Visible = _spritesVisible };
        if (instance.Layer == BattleSpriteLayer.Front)
        {
            _front.AddChild(node);
        }
        else
        {
            _models.AddOverlay(node);
        }

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
        var placed = active.Where(_nodes.ContainsKey).Select(instance => (Instance: instance, Origin: Project(instance.Anchor, instance.Layer, out int entry), Entry: entry)).ToArray();
        foreach (var item in placed)
        {
            _nodes[item.Instance].Render(item.Origin, BattleHudLayout.Scale);
        }

        var models = DrawOrder(placed.Where(item => item.Instance.Layer == BattleSpriteLayer.Models).Select(item => (item.Instance, item.Entry)));
        _models.PlaceOverlays([.. models.Select(item => ((Node2D)_nodes[item.Instance], item.Entry))]);
        int index = 0;
        foreach ((BattleSpriteEffectInstance instance, _) in DrawOrder(placed.Where(item => item.Instance.Layer == BattleSpriteLayer.Front).Select(item => (item.Instance, item.Entry))))
        {
            _front.MoveChild(_nodes[instance], index++);
        }

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

        _models.PlaceOverlays([]);
        _front.QueueFree();
        _flash.QueueFree();
        _textures.Dispose();
        _materials.Dispose();
    }

    /// <summary>
    /// Orders the effects of one layer like the source ordering table: the farthest entry first, and within one
    /// entry the effect created last first, because each new primitive is linked ahead of the earlier ones.
    /// </summary>
    /// <param name="items">Effects of one layer with their ordering-table entry.</param>
    /// <returns>The effects in draw order.</returns>
    private static IEnumerable<(BattleSpriteEffectInstance Instance, int Entry)> DrawOrder(IEnumerable<(BattleSpriteEffectInstance Instance, int Entry)> items) =>
        items.OrderByDescending(item => item.Entry).ThenByDescending(item => item.Instance.Serial);

    /// <summary>
    /// Projects an anchor to the canvas: world points through the battle camera, screen points through the
    /// 3x layout from their layer's drawing offset (the models layer is centred at (160, 120), the front layer is not).
    /// </summary>
    /// <param name="anchor">Script position.</param>
    /// <param name="layer">Source layer of the effect.</param>
    /// <param name="entry">Source ordering-table entry: the projected depth's entry, or the front or back entry of a screen marker.</param>
    /// <returns>Canvas pivot.</returns>
    private Vector2 Project(BattleSpriteAnchor anchor, BattleSpriteLayer layer, out int entry)
    {
        if (anchor.IsScreen)
        {
            entry = anchor.Z == BattleSpriteAnchor.FrontScreenMarker ? BattleSpriteOrdering.FrontEntry : BattleSpriteOrdering.BackEntry;
            int offsetX = layer == BattleSpriteLayer.Models ? BattleHudLayout.SourceWidth / 2 : 0;
            int offsetY = layer == BattleSpriteLayer.Models ? BattleHudLayout.SourceHeight / 2 : 0;
            (int x, int y) = BattleHudLayout.ToViewport(anchor.X + offsetX, anchor.Y + offsetY);
            return new Vector2(x, y);
        }

        Vector3 world = BattleModel.Ps1Position(new Vector3(anchor.X, anchor.Y, anchor.Z));
        int sz = (int)MathF.Round(-(_camera.GlobalTransform.AffineInverse() * world).Z * BattleSceneMath.Ps1UnitsPerGodotUnit);
        entry = BattleSpriteOrdering.PointEntry(sz);
        return _camera.UnprojectPosition(world);
    }
}
