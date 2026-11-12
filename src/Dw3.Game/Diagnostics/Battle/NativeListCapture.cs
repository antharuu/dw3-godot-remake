using Dw3Production.Conversion;
using Dw3Production.Diagnostics.Field;
using Dw3Production.Presentation;
using Dw3Production.Simulation;
using Godot;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>Captures the actual HUD draw submission and checks opaque glyph texels against decoded masks.</summary>
internal sealed class NativeListCapture : IDisposable
{
    private readonly Node _owner;
    private readonly FontData _font;
    private readonly SubViewport _viewport;
    private readonly SubViewport _oracle;
    private readonly BattleHudNode _hud;
    private readonly NativeListGlyphMask _mask;
    private readonly string _root;
    private readonly BattleMenuIconsData _icons;

    /// <summary>Initializes a new instance of the <see cref="NativeListCapture"/> class.</summary>
    /// <param name="owner">Isolated scene owning both offscreen targets.</param>
    /// <param name="loader">Prepared battle assets.</param>
    /// <param name="font">Prepared font.</param>
    /// <param name="root">Private evidence directory.</param>
    public NativeListCapture(Node owner, BattleDataPackLoader loader, FontData font, string root)
    {
        _owner = owner;
        _font = font;
        _root = root;
        _viewport = Target(owner);
        _oracle = Target(owner);
        _hud = new BattleHudNode();
        _mask = new NativeListGlyphMask();
        _viewport.AddChild(_hud);
        _oracle.AddChild(_mask);
        _hud.SetFont(font);
        _hud.SetHud(loader.LoadBattleHud());
        _icons = loader.LoadBattleMenuIcons();
        _hud.SetMenuIcons(_icons);
        _hud.SetProbeAnimationMilliseconds(64);
        Directory.CreateDirectory(root);
    }

    /// <summary>Submits the production view, awaits rendering and saves its actual pixels.</summary>
    /// <param name="session">Native host whose menu and flow are rendered.</param>
    /// <param name="name">Private screenshot basename.</param>
    /// <returns>Read-back pixels after frame submission.</returns>
    public async Task<Image> SaveAsync(NativeMenuSession session, string name)
    {
        var flow = session.Flow;
        _hud.SetView(BattleViewBuilder.Build(flow, session.Menu.View, null, id => session.Text.DigimonByNameId(flow.NameIds.GetValueOrDefault(id))));
        await DrawnAsync();
        var image = _viewport.GetTexture().GetImage();
        ProbeRequirement.Require(image.SavePng(Path.Combine(_root, name + ".png")) == Error.Ok, "capture write failed");
        return image;
    }

    /// <summary>Checks every opaque expected glyph pixel, not merely the line count or panel visibility.</summary>
    /// <param name="actual">Actual submitted HUD pixels.</param>
    /// <param name="text">Expected pack token stream.</param>
    /// <param name="anchor">Source text position.</param>
    /// <param name="palette">Expected CLUT row.</param>
    /// <returns>Completion after readback and exact opaque glyph assertion.</returns>
    public async Task GlyphsAsync(Image actual, BattleTextLine text, Vector2I anchor, int palette)
    {
        _mask.Bind(_font, text, anchor, palette);
        await AssertMaskAsync(actual, $"at={anchor} palette={palette} text={text.Debug}");
    }

    /// <summary>Checks the numeric MP cost actually submitted by the list painter.</summary>
    /// <param name="actual">Submitted HUD pixels.</param>
    /// <param name="cost">Expected prepared cost.</param>
    /// <returns>Completion after mask comparison.</returns>
    public async Task CostAsync(Image actual, int cost)
    {
        _mask.BindWindow(_font, cost.ToString(System.Globalization.CultureInfo.InvariantCulture), new Vector2I(0x12B, 0xD0), 1, true);
        await AssertMaskAsync(actual, $"cost={cost}");
    }

    /// <summary>Checks the active MP digits submitted in the small source font.</summary>
    /// <param name="actual">Submitted HUD pixels.</param>
    /// <param name="mp">Active MP.</param>
    /// <returns>Completion after mask comparison.</returns>
    public async Task MpAsync(Image actual, int mp)
    {
        _mask.BindWindow(_font, mp.ToString(System.Globalization.CultureInfo.InvariantCulture), new Vector2I(0xD8, 0x3A), 3, true);
        await AssertMaskAsync(actual, $"current-mp={mp}");
    }

    /// <summary>Checks a source-derived skill marker against the submitted pixels.</summary>
    /// <param name="actual">Submitted HUD pixels.</param>
    /// <param name="icon">Pack-derived marker frame.</param>
    /// <param name="row">Visible technique row.</param>
    /// <returns>Completion after marker comparison.</returns>
    public async Task IconAsync(Image actual, int icon, int row)
    {
        _mask.BindIcon(_icons, icon, new Vector2I(0x1D, 0x45 + (row * 14)));
        await AssertMaskAsync(actual, $"icon={icon} row={row}");
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _viewport.QueueFree();
        _oracle.QueueFree();
    }

    /// <summary>Creates a real offscreen render target without touching an OS window.</summary>
    /// <param name="owner">Owner in the isolated scene.</param>
    /// <returns>Transparent source-resolution target.</returns>
    private static SubViewport Target(Node owner)
    {
        var target = new SubViewport { Size = new Vector2I(320, 240), TransparentBg = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        owner.AddChild(target);
        return target;
    }

    /// <summary>Compares every opaque texel in the independently submitted mask.</summary>
    /// <param name="actual">Production pixels.</param>
    /// <param name="subject">Expected content identity.</param>
    /// <returns>Completion after a render and readback.</returns>
    private async Task AssertMaskAsync(Image actual, string subject)
    {
        await DrawnAsync();
        using var expected = _oracle.GetTexture().GetImage();
        int count = 0;
        int mismatch = 0;
        for (int y = 0; y < expected.GetHeight(); y++)
        {
            for (int x = 0; x < expected.GetWidth(); x++)
            {
                Color pixel = expected.GetPixel(x, y);
                if (pixel.A8 == 255)
                {
                    count++;
                    mismatch += pixel.ToRgba32() == actual.GetPixel(x, y).ToRgba32() ? 0 : 1;
                }
            }
        }

        ProbeRequirement.Require(count > 8 && mismatch == 0, $"glyph submission {subject} opaque={count} mismatch={mismatch}");
        GD.Print($"NATIVE_LIST_GLYPHS {subject} opaque={count} mismatch={mismatch}");
    }

    /// <summary>Waits for an actual renderer submission, not just a process tick.</summary>
    /// <returns>Completion after the rendering server frame.</returns>
    private async Task DrawnAsync()
    {
        await _owner.ToSignal(_owner.GetTree(), SceneTree.SignalName.ProcessFrame);
        await _owner.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
    }
}
