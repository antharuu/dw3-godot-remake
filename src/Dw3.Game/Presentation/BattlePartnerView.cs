using Godot;

namespace Dw3Production.Presentation;

/// <summary>
/// The small partner view shown during the player's command turn: a separate 100x60 render target
/// holding a second instance of the active player's model, drawn over the HUD at source position
/// (208, 76). Godot cannot render one Node3D through two cameras at independent sizes, so this
/// world carries its own copy, lit and posed like the main one.
/// </summary>
internal sealed class BattlePartnerView : IDisposable
{
    private const int Width = 100;
    private const int Height = 60;

    /// <summary>Half the target height in pixels, which the projection distance maps to.</summary>
    private const float HalfHeight = 30f;

    private readonly SubViewport _viewport;
    private readonly Node3D _world;
    private readonly Camera3D _camera;
    private readonly TextureRect _screen;

    private readonly BattleModelLayer _modelLayer;
    private BattleModel? _model;

    /// <summary>
    /// Initializes a new instance of the <see cref="BattlePartnerView"/> class and adds its render
    /// target and on-screen rectangle to <paramref name="layer"/>.
    /// </summary>
    /// <param name="layer">The battle canvas layer.</param>
    public BattlePartnerView(CanvasLayer layer)
    {
        _viewport = new SubViewport
        {
            Size = new Vector2I(Width, Height),
            TransparentBg = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            OwnWorld3D = true,
            HandleInputLocally = false,
        };
        _world = new Node3D { Name = "PartnerViewWorld" };
        _viewport.AddChild(_world);
        var rasterLayer = new CanvasLayer { Layer = 1 };
        _viewport.AddChild(rasterLayer);
        _modelLayer = new BattleModelLayer(rasterLayer, "PartnerModelOrderingLayer");
        _camera = new Camera3D { Current = true, KeepAspect = Camera3D.KeepAspectEnum.Height };
        _world.AddChild(_camera);
        _screen = new TextureRect
        {
            Texture = _viewport.GetTexture(),
            Position = new Vector2((208 * BattleHudLayout.Scale) + BattleHudLayout.OffsetX, 76 * BattleHudLayout.Scale),
            Size = new Vector2(Width * BattleHudLayout.Scale, Height * BattleHudLayout.Scale),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
        };
        layer.AddChild(_viewport);
        layer.AddChild(_screen);
    }

    /// <summary>Gets the partner model, or null when it is not built.</summary>
    public BattleModel? Model => _model;

    /// <summary>Gets or sets a value indicating whether the view rectangle is on screen.</summary>
    public bool Visible
    {
        get => _screen.Visible;
        set => _screen.Visible = value;
    }

    /// <summary>Moves the rectangle above every other layer child. Its source layer is created after
    /// the screen, menu and front layers, so it draws over the HUD frame that surrounds it.</summary>
    public void MoveScreenToFront() => _screen.GetParent().MoveChild(_screen, _screen.GetParent().GetChildCount() - 1);

    /// <summary>Loads the partner copy of the player model, placed and posed like the main one.</summary>
    /// <param name="jsonPath">Player model JSON.</param>
    /// <param name="fighterId">Player fighter id, used in the node name and the warning.</param>
    /// <returns>False (after a logged warning) when the model cannot be loaded.</returns>
    public bool TryBuild(string jsonPath, int fighterId)
    {
        try
        {
            _model = BattleModel.Load(_world, jsonPath);
            _model.Root.Name = $"PartnerView#{fighterId}";
            var placement = BattleSceneMath.FighterPlacement(enemy: false, _model.Distance, _model.Height);
            _model.Root.Position = new Vector3(placement.Position.X, placement.Position.Y, placement.Position.Z);
            _model.Root.Rotation = new Vector3(0f, placement.YawRadians, 0f);
            _model.Step(0);
            return true;
        }
        catch (Exception error) when (error is InvalidOperationException or FileNotFoundException or System.Text.Json.JsonException)
        {
            _model = null;
            GD.PushWarning($"WARN battle-partner-view-model-failed fighter={fighterId} reason={error.Message}");
            return false;
        }
    }

    /// <summary>Frees the partner model and hides the view.</summary>
    public void Clear()
    {
        _modelLayer.Clear();
        _model?.Root.QueueFree();
        _model = null;
        _screen.Visible = false;
    }

    /// <summary>
    /// Points the camera at view 6, or view 7 while the player's live idle motion is non-zero. The
    /// projection distance maps to the 30-pixel vertical half-height of the 100x60 target. The
    /// view is hidden when the model or that camera is missing.
    /// </summary>
    /// <param name="playerIdleMotion">Idle selection of the main player model, not of this copy.</param>
    public void AimCamera(int playerIdleMotion)
    {
        var viewIndex = BattleSceneMath.PartnerCameraViewIndex(playerIdleMotion);
        if (_model is null || _model.Cameras.Count <= viewIndex)
        {
            _screen.Visible = false;
            return;
        }

        var view = _model.Cameras[viewIndex];
        var position = BattleSceneMath.CameraPosition(view.CamPos[0], view.CamPos[1], view.CamPos[2]);
        var reference = BattleSceneMath.CameraPosition(view.CamRef[0], view.CamRef[1], view.CamRef[2]);
        _camera.Position = new Vector3(position.X, position.Y, position.Z);
        _camera.LookAt(new Vector3(reference.X, reference.Y, reference.Z), Vector3.Up);
        _camera.Fov = BattleSceneMath.VerticalFovDegrees(view.Proj, HalfHeight);
    }

    /// <summary>Rebuilds the ordering-table mesh of the partner model, or clears it.</summary>
    public void Rebuild()
    {
        if (_model is not null)
        {
            _modelLayer.Rebuild(_camera, [_model]);
        }
        else
        {
            _modelLayer.Clear();
        }
    }

    /// <summary>Counts opaque pixels of the dedicated target, independently of the main framebuffer.</summary>
    /// <returns>Zero when the view is hidden.</returns>
    public int OpaquePixels()
    {
        if (!_screen.Visible)
        {
            return 0;
        }

        var image = _viewport.GetTexture().GetImage();
        var count = 0;
        for (var y = 0; y < image.GetHeight(); y++)
        {
            for (var x = 0; x < image.GetWidth(); x++)
            {
                count += image.GetPixel(x, y).A8 > 0 ? 1 : 0;
            }
        }

        return count;
    }

    /// <summary>Writes the raw 100x60 target as a PNG.</summary>
    /// <param name="path">Destination file.</param>
    /// <returns>The Godot save result.</returns>
    public Error Save(string path) => _viewport.GetTexture().GetImage().SavePng(path);

    /// <summary>Frees the render target and the on-screen rectangle.</summary>
    public void Dispose()
    {
        _viewport.QueueFree();
        _screen.QueueFree();
    }
}
