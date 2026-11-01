using Dw3Production.Conversion;
using Godot;

namespace Dw3Production.Presentation;

/// <summary>
/// The decoded battle HUD sprites: the battle menu sheet frames (one texture per CLUT row, with the
/// crop origin the decoder recorded), the 12-frame menu cursor and its "off" image, and the menu
/// icons. A missing HUD frame is logged and skipped, never silently drawn.
/// </summary>
internal sealed class BattleHudSprites : IDisposable
{
    private const int CursorSize = 12;

    private readonly Dictionary<int, ImageTexture[]> _frameTextures = [];
    private readonly Dictionary<int, Vector2> _frameOffsets = [];
    private readonly Dictionary<int, ImageTexture[]> _menuIconTextures = [];
    private ImageTexture? _cursor;
    private ImageTexture? _cursorOff;

    /// <summary>Gets the bound menu icon set, which also carries the message arrow placement.</summary>
    public BattleMenuIconsData? MenuIcons { get; private set; }

    /// <summary>Replaces the HUD frames and cursor images; null leaves none.</summary>
    /// <param name="hud">Decoded HUD frames and cursor paths.</param>
    public void LoadHud(BattleHudData? hud)
    {
        _frameTextures.Clear();
        _frameOffsets.Clear();
        _cursor = null;
        _cursorOff = null;
        if (hud is null)
        {
            return;
        }

        foreach (var (id, frame) in hud.Frames)
        {
            LoadFrame(id, frame);
        }

        _cursor = LoadCursor(hud.Cursor.Path, "battle-hud-cursor");
        _cursorOff = LoadCursor(hud.Cursor.OffPath, "battle-hud-cursor-off");
    }

    /// <summary>Replaces the menu icons.</summary>
    /// <param name="icons">Decoded icon frames and the message arrow placement.</param>
    /// <exception cref="InvalidOperationException">An icon image is unreadable.</exception>
    public void LoadMenuIcons(BattleMenuIconsData icons)
    {
        MenuIcons = icons;
        _menuIconTextures.Clear();
        foreach (var (id, frame) in icons.Frames)
        {
            var images = frame.ClutRows.Select(Image.LoadFromFile).ToArray();
            if (images.Any(image => image is null || image.IsEmpty()))
            {
                throw new InvalidOperationException($"menu icon image unreadable frame={id}");
            }

            _menuIconTextures[id] = [.. images.Select(image => ImageTexture.CreateFromImage(image))];
        }
    }

    /// <summary>Draws a sheet frame at its source anchor plus its recorded crop origin; an unloaded frame draws nothing.</summary>
    /// <param name="canvas">Canvas inside its draw callback.</param>
    /// <param name="frameId">Frame id.</param>
    /// <param name="x">Source x of the anchor.</param>
    /// <param name="y">Source y of the anchor.</param>
    /// <param name="clutRow">CLUT row variant, clamped to the decoded rows.</param>
    public void DrawFrame(CanvasItem canvas, int frameId, int x, int y, int clutRow)
    {
        if (!_frameTextures.TryGetValue(frameId, out var rows))
        {
            return;
        }

        var offset = _frameOffsets.TryGetValue(frameId, out var value) ? value : Vector2.Zero;
        canvas.DrawTexture(rows[Math.Clamp(clutRow, 0, rows.Length - 1)], new Vector2(x, y) + offset, Colors.White);
    }

    /// <summary>Draws a menu icon at its source anchor plus its frame offset; an unknown icon draws nothing.</summary>
    /// <param name="canvas">Canvas inside its draw callback.</param>
    /// <param name="frameId">Icon frame id.</param>
    /// <param name="x">Source x of the anchor.</param>
    /// <param name="y">Source y of the anchor.</param>
    /// <param name="clutRow">CLUT row variant, clamped to the decoded rows.</param>
    public void DrawMenuIcon(CanvasItem canvas, int frameId, int x, int y, int clutRow)
    {
        if (MenuIcons is null || !MenuIcons.Frames.TryGetValue(frameId, out var frame)
            || !_menuIconTextures.TryGetValue(frameId, out var rows))
        {
            return;
        }

        canvas.DrawTexture(rows[Math.Clamp(clutRow, 0, rows.Length - 1)], new Vector2(x + frame.OffsetX, y + frame.OffsetY));
    }

    /// <summary>Draws one 12x12 cell of the cursor strip; nothing when the cursor is unreadable.</summary>
    /// <param name="canvas">Canvas inside its draw callback.</param>
    /// <param name="position">Source top-left corner.</param>
    /// <param name="frame">Cell index 0..11 in the strip.</param>
    public void DrawCursor(CanvasItem canvas, Vector2 position, int frame)
    {
        if (_cursor is not null)
        {
            canvas.DrawTextureRectRegion(_cursor, new Rect2(position, CursorSize, CursorSize), new Rect2(frame * CursorSize, 0, CursorSize, CursorSize));
        }
    }

    /// <summary>Draws the cursor's "off" image, which erases the previous cursor position.</summary>
    /// <param name="canvas">Canvas inside its draw callback.</param>
    /// <param name="position">Source top-left corner.</param>
    public void DrawCursorOff(CanvasItem canvas, Vector2 position)
    {
        if (_cursorOff is not null)
        {
            canvas.DrawTexture(_cursorOff, position);
        }
    }

    /// <summary>Releases every loaded texture.</summary>
    public void Dispose()
    {
        foreach (var texture in _frameTextures.Values.Concat(_menuIconTextures.Values).SelectMany(rows => rows))
        {
            texture.Dispose();
        }

        _cursor?.Dispose();
        _cursorOff?.Dispose();
        _frameTextures.Clear();
        _menuIconTextures.Clear();
    }

    private static ImageTexture? LoadCursor(string path, string warningKey)
    {
        var image = Image.LoadFromFile(path);
        if (image is null || image.IsEmpty())
        {
            GD.PushWarning($"WARN {warningKey}-unreadable path={path}");
            return null;
        }

        return ImageTexture.CreateFromImage(image);
    }

    private void LoadFrame(int id, BattleHudFrame frame)
    {
        var rows = frame.ClutRows.Select(path => Image.LoadFromFile(path)).ToArray();
        if (rows.Any(image => image is null || image.IsEmpty()))
        {
            GD.PushWarning($"WARN battle-hud-frame-unreadable frame={id}");
            return;
        }

        _frameTextures[id] = [.. rows.Select(image => ImageTexture.CreateFromImage(image))];
        _frameOffsets[id] = new Vector2(frame.OffsetX, frame.OffsetY);
    }
}
