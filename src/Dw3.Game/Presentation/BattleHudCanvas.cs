using Godot;

namespace Dw3Production.Presentation;

/// <summary>
/// What a HUD painter needs while the HUD control draws: the control to draw on, its decoded
/// sprites and text, and the animation clock. Coordinates are source 320x240 pixels; the control's
/// own transform scales them to the viewport.
/// </summary>
/// <param name="Canvas">The HUD control, valid only inside its draw callback.</param>
/// <param name="Sprites">Decoded HUD sprite frames, cursor and menu icons.</param>
/// <param name="Text">PSX text drawing.</param>
/// <param name="Clock">Animation clock.</param>
internal readonly record struct BattleHudCanvas(CanvasItem Canvas, BattleHudSprites Sprites, BattleHudText Text, BattleHudClock Clock)
{
    /// <summary>Gets the 12-frame cursor animation index, advancing every 16 ms.</summary>
    public int CursorFrame => (int)((Clock.Milliseconds / 16) % 12);

    /// <summary>Gets the selected-line CLUT row, cycling over five rows every four 16 ms frames.</summary>
    public int BlinkRow => (int)((Clock.Milliseconds / 16 / 4) % 5);

    /// <summary>Draws one sprite frame.</summary>
    /// <param name="frameId">Frame id in the battle menu sheet.</param>
    /// <param name="x">Source x of the frame anchor.</param>
    /// <param name="y">Source y of the frame anchor.</param>
    /// <param name="clutRow">CLUT row variant, clamped to the decoded rows.</param>
    public void Frame(int frameId, int x, int y, int clutRow = 0) => Sprites.DrawFrame(Canvas, frameId, x, y, clutRow);

    /// <summary>Draws one menu icon.</summary>
    /// <param name="frameId">Icon frame id.</param>
    /// <param name="x">Source x of the icon anchor.</param>
    /// <param name="y">Source y of the icon anchor.</param>
    /// <param name="clutRow">CLUT row variant, clamped to the decoded rows.</param>
    public void MenuIcon(int frameId, int x, int y, int clutRow = 0) => Sprites.DrawMenuIcon(Canvas, frameId, x, y, clutRow);

    /// <summary>Draws the animated 12x12 cursor at its current frame.</summary>
    /// <param name="x">Source x of the cursor's top-left corner.</param>
    /// <param name="y">Source y of the cursor's top-left corner.</param>
    public void Cursor(int x, int y) => Sprites.DrawCursor(Canvas, new Vector2(x, y), CursorFrame);
}
