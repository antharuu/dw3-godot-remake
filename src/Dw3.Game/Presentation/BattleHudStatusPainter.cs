using Dw3Production.Simulation;
using Godot;

namespace Dw3Production.Presentation;

/// <summary>
/// Draws the always-visible part of the battle HUD in source 320x240 coordinates: the two HP-bar
/// windows, the background and technique gauge, the HP bars and numbers, the fighter names, the
/// team icons and the action message.
/// </summary>
internal static class BattleHudStatusPainter
{
    // Bar corners (top-left, top-right, bottom-left, bottom-right). The player's bar shrinks toward
    // its left end, the enemy's toward its right end.
    private static readonly Vector2[] PlayerBar = [new(0xAF, 0x25), new(0x12F, 0x25), new(0xAF, 0x2B), new(0x12F, 0x2B)];
    private static readonly Vector2[] EnemyBar = [new(0x8F, 0x25), new(0x0F, 0x25), new(0x8F, 0x2B), new(0x0F, 0x2B)];

    // Above a quarter of the maximum HP the bar is green, at or below it red; each has a dark and a light tone.
    private static readonly Color GreenDark = new(0.0f, 0.44f, 0.16f);
    private static readonly Color GreenLight = new(0.0f, 0.78f, 0.24f);
    private static readonly Color RedDark = new(0.70f, 0.22f, 0.09f);
    private static readonly Color RedLight = new(1.0f, 0.22f, 0.05f);

    // Team icon x positions per slot: player icons left to right, enemy icons right to left.
    private static readonly int[][] TeamIconX = [[0x10F, 0x11A, 0x125], [0x25, 0x1A, 0x0F]];

    /// <summary>Draws every status element in source order.</summary>
    /// <param name="canvas">The HUD drawing context.</param>
    /// <param name="view">The battle view of this frame.</param>
    public static void Draw(BattleHudCanvas canvas, BattleView view)
    {
        DrawHudFrames(canvas, view);
        DrawHpBar(canvas.Canvas, side: 0, view.Players);
        DrawHpBar(canvas.Canvas, side: 1, view.Enemies);
        DrawFighterNames(canvas, view);
        DrawTeamIcons(canvas, view);
        DrawHpNumbers(canvas, view);
        DrawMessage(canvas, view);
    }

    /// <summary>The HP-bar window frames (0 at (8, 0xF), 1 at (0xA1, 0xF)), the background frame
    /// 0xB, the technique gauge (0x33, or 0x37 once MP is full, cycling four frames) and, during the
    /// command turn, frame 10 around the partner view.</summary>
    /// <param name="canvas">The HUD drawing context.</param>
    /// <param name="view">The battle view; its active player selects the gauge frame.</param>
    private static void DrawHudFrames(BattleHudCanvas canvas, BattleView view)
    {
        canvas.Frame(0, 8, 0xF);
        canvas.Frame(1, 0xA1, 0xF);
        canvas.Frame(0x1E, 0x104, 0x3D);
        var player = view.Players.FirstOrDefault(fighter => fighter.Active);
        var gaugeFrame = player.MaxMp > 0 && player.Mp >= player.MaxMp ? 0x37 : 0x33;
        canvas.Frame(gaugeFrame + (int)((canvas.Clock.Milliseconds / 16 / 4) & 3), 0x109, 0x3E);
        canvas.Frame(0xB, 0x104, 0x3D);
        if (view.Phase == BattleFlowPhase.PlayerCommand)
        {
            canvas.Frame(10, 246, 74);
        }
    }

    /// <summary>The active fighter's HP bar of one side.</summary>
    /// <param name="canvas">The HUD control inside its draw callback.</param>
    /// <param name="side">0 for the player bar (top right), 1 for the enemy bar (top left).</param>
    /// <param name="fighters">That side's fighters; nothing is drawn without an active one.</param>
    private static void DrawHpBar(CanvasItem canvas, int side, IReadOnlyList<BattleFighterView> fighters)
    {
        var active = fighters.FirstOrDefault(fighter => fighter.Active);
        if (active.Id == 0)
        {
            return;
        }

        var points = side == 0 ? PlayerBar : EnemyBar;
        var left = points[0];
        var right = points[1];
        var top = points[0].Y;
        var height = points[2].Y - top;
        var ratio = active.MaxHp > 0 ? Mathf.Clamp((float)active.Hp / active.MaxHp, 0f, 1f) : 0f;
        var (dark, light) = active.Hp > active.MaxHp / 4 ? (GreenDark, GreenLight) : (RedDark, RedLight);
        var filled = (right.X - left.X) * ratio;
        canvas.DrawRect(BarRect(side, left, right, top, filled, height), light);

        // The source fill is a two-colour gradient quad; its dark end is approximated by a thin
        // darker strip at the low end.
        canvas.DrawRect(BarRect(side, left, right, top, Mathf.Min(filled, 6f), height), dark);
    }

    private static Rect2 BarRect(int side, Vector2 left, Vector2 right, float top, float width, float height) => side == 0
        ? new Rect2(new Vector2(right.X - width, top), new Vector2(width, height))
        : new Rect2(new Vector2(left.X, top), new Vector2(width, height));

    /// <summary>The player's current HP at 0x10B and maximum HP at 0x12E, both style-3 windows.</summary>
    /// <param name="canvas">The HUD drawing context.</param>
    /// <param name="view">The battle view; nothing is drawn without an active player.</param>
    private static void DrawHpNumbers(BattleHudCanvas canvas, BattleView view)
    {
        var player = view.Players.FirstOrDefault(fighter => fighter.Active);
        if (player.Id != 0)
        {
            canvas.Text.DrawWindow(3, 0x10B, 0x1A, $"{player.Hp}", rightAlign: true, palette: BattleHudPalette.White);
            canvas.Text.DrawWindow(3, 0x12E, 0x1A, $"{player.MaxHp}", rightAlign: true, palette: BattleHudPalette.White);
        }
    }

    private static void DrawFighterNames(BattleHudCanvas canvas, BattleView view)
    {
        var player = view.Players.FirstOrDefault(fighter => fighter.Active);
        var enemy = view.Enemies.FirstOrDefault(fighter => fighter.Active);
        if (player.Id != 0)
        {
            canvas.Text.DrawLine(1, 0xAE, 0x15, player.Name, string.Empty, rightAlign: false, palette: BattleHudPalette.White);
        }

        if (enemy.Id != 0)
        {
            canvas.Text.DrawLine(1, 0x11, 0x15, enemy.Name, string.Empty, rightAlign: false, palette: BattleHudPalette.White);
        }
    }

    /// <summary>One icon per team slot (up to three per side) at y 0x30.</summary>
    /// <param name="canvas">The HUD drawing context.</param>
    /// <param name="view">The battle view holding both teams.</param>
    private static void DrawTeamIcons(BattleHudCanvas canvas, BattleView view)
    {
        DrawTeamSide(canvas, view.Players, TeamIconX[0]);
        DrawTeamSide(canvas, view.Enemies, TeamIconX[1]);
    }

    private static void DrawTeamSide(BattleHudCanvas canvas, IReadOnlyList<BattleFighterView> fighters, int[] iconX)
    {
        for (var slot = 0; slot < Math.Min(3, fighters.Count); slot++)
        {
            var fighter = fighters[slot];
            if (fighter.Id != 0)
            {
                canvas.Frame(TeamIconFrame(fighter), iconX[slot], 0x30);
            }
        }
    }

    /// <summary>Icon frame: base 0 healthy, 1 hurt, 2 knocked out; plus 0xC when active, 0x3C otherwise.</summary>
    /// <param name="fighter">A present team member.</param>
    /// <returns>The battle menu sheet frame id.</returns>
    private static int TeamIconFrame(BattleFighterView fighter)
    {
        var baseFrame = fighter.KnockedOut ? 2 : fighter.Hp != fighter.MaxHp ? 1 : 0;
        return baseFrame + (fighter.Active ? 0xC : 0x3C);
    }

    /// <summary>The message window (frame 0x31 at (0xB, 0xBC)), its two lines at (0x14, 0xC2) and
    /// (0x14, 0xD0), and the continuation arrow whose CLUT row steps every arrow time step frames,
    /// wrapping at five.</summary>
    /// <param name="canvas">The HUD drawing context.</param>
    /// <param name="view">The battle view; nothing is drawn while its message is hidden.</param>
    private static void DrawMessage(BattleHudCanvas canvas, BattleView view)
    {
        if (!view.MessageVisible)
        {
            return;
        }

        canvas.Frame(0x31, 0xB, 0xBC);
        canvas.Text.DrawLine(1, 0x14, 0xC2, view.MessageText, view.Message, rightAlign: false, palette: BattleHudPalette.White);
        if (view.MessageText?.Second is { } second)
        {
            canvas.Text.DrawLine(1, 0x14, 0xD0, second, second.Debug, rightAlign: false, palette: BattleHudPalette.White);
        }

        if (canvas.Sprites.MenuIcons is { } icons)
        {
            canvas.MenuIcon(icons.ArrowFrame, icons.ArrowX, icons.ArrowY, (int)(canvas.Clock.Milliseconds / 16UL / (ulong)icons.ArrowTimeStep) % 5);
        }
    }
}
