using Dw3Production.Conversion;
using Dw3Production.Simulation;
using Godot;

namespace Dw3Production.Presentation;

/// <summary>
/// The battle HUD control. It draws in source 320x240 coordinates, scaled once by its own node
/// transform: the status elements always, and the command menu during the player's command turn.
/// </summary>
internal sealed partial class BattleHudNode : Control
{
    private readonly BattleHudText _text;
    private readonly BattleHudSprites _sprites = new();
    private readonly BattleHudClock _clock = new();
    private readonly BattleCommandMenuPainter _menus = new();
    private BattleView? _view;

    /// <summary>
    /// Initializes a new instance of the <see cref="BattleHudNode"/> class, filling its parent and
    /// ignoring the mouse.
    /// </summary>
    public BattleHudNode()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        _text = new BattleHudText(this);
    }

    /// <summary>Checks that every HUD element fits the source 320x240 screen and that the 12x12
    /// cursor maps to 36x36 viewport pixels.</summary>
    /// <returns>A one-line summary of the checked layout.</returns>
    /// <exception cref="InvalidOperationException">A bound or the cursor size differs.</exception>
    public static string AssertLayout()
    {
        if (!BattleHudLayout.FitsSourceRect(8, 15, 151, 43)
            || !BattleHudLayout.FitsSourceRect(161, 15, 151, 43)
            || !BattleHudLayout.FitsSourceRect(11, 188, 298, 40)
            || !BattleHudLayout.FitsSourceRect(34, 16, 12, 12))
        {
            throw new InvalidOperationException("battle HUD source bounds exceed 320x240");
        }

        var cursor = BattleHudLayout.ToViewportSize(12, 12);
        if (cursor != (36, 36))
        {
            throw new InvalidOperationException($"menu cursor viewport size={cursor.Width}x{cursor.Height}, expected=36x36");
        }

        return $"hud=320x240 scale={BattleHudLayout.Scale} offset=({BattleHudLayout.OffsetX},{BattleHudLayout.OffsetY}) cursor=12x12->36x36 bounds=inside";
    }

    /// <summary>Binds the decoded font atlas.</summary>
    /// <param name="font">Decoded font metrics and pages.</param>
    public void SetFont(FontData font)
    {
        _text.SetFont(font);
        QueueRedraw();
    }

    /// <summary>Binds the decoded HUD frames and cursor; a missing frame is logged and skipped.</summary>
    /// <param name="hud">Decoded HUD sprites, or null for none.</param>
    public void SetHud(BattleHudData? hud)
    {
        _sprites.LoadHud(hud);
        QueueRedraw();
    }

    /// <summary>Binds the menu icons and the message arrow.</summary>
    /// <param name="icons">Decoded icon frames.</param>
    /// <exception cref="InvalidOperationException">An icon image is unreadable.</exception>
    public void SetMenuIcons(BattleMenuIconsData icons)
    {
        _sprites.LoadMenuIcons(icons);
        QueueRedraw();
    }

    /// <summary>Pins or releases the animation clock.</summary>
    /// <param name="milliseconds">Pinned time, or null to follow the engine clock.</param>
    public void SetProbeAnimationMilliseconds(ulong? milliseconds)
    {
        _clock.ProbeMilliseconds = milliseconds;
        QueueRedraw();
    }

    /// <summary>Shows a battle view, or nothing for null.</summary>
    /// <param name="view">The battle view of this frame.</param>
    public void SetView(BattleView? view)
    {
        _view = view;
        QueueRedraw();
    }

    /// <summary>Draws the bound view.</summary>
    public override void _Draw()
    {
        if (_view is not { } view)
        {
            return;
        }

        var canvas = new BattleHudCanvas(this, _sprites, _text, _clock);
        BattleHudStatusPainter.Draw(canvas, view);
        if (view.Phase == BattleFlowPhase.PlayerCommand)
        {
            _menus.Draw(canvas, view);
        }
    }

    /// <summary>Releases the HUD textures together with the control.</summary>
    /// <param name="disposing">True when called from <see cref="IDisposable.Dispose"/>.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _sprites.Dispose();
        }

        base.Dispose(disposing);
    }
}
