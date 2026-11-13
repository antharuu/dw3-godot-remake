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
    private static readonly Color GreenDark = Color.Color8(0, 113, 40);
    private static readonly Color GreenLight = Color.Color8(0, 200, 62);
    private static readonly Color RedDark = Color.Color8(179, 55, 22);
    private static readonly Color RedLight = Color.Color8(255, 55, 13);

    // Team icon x positions per slot: player icons left to right, enemy icons right to left.
    private static readonly int[][] TeamIconX = [[0x10F, 0x11A, 0x125], [0x25, 0x1A, 0x0F]];

    /// <summary>Draws every status element in source order.</summary>
    /// <param name="canvas">The HUD drawing context.</param>
    /// <param name="view">The battle view of this frame.</param>
    /// <param name="hpDisplay">Persistent source HP-display state.</param>
    public static void Draw(BattleHudCanvas canvas, BattleView view, BattleHudHpDisplay hpDisplay)
    {
        DrawHudFrames(canvas, view);
        DrawHpBar(canvas.Canvas, side: 0, view.Players, hpDisplay, canvas.Clock.Milliseconds);
        DrawHpBar(canvas.Canvas, side: 1, view.Enemies, hpDisplay, canvas.Clock.Milliseconds);
        DrawFighterNames(canvas, view);
        DrawTeamIcons(canvas, view);
        DrawHpNumbers(canvas, view, hpDisplay);
        DrawMessage(canvas, view);
    }

    /// <summary>The HP-bar window frames (0 at (8, 0xF), 1 at (0xA1, 0xF)), the background frame
    /// 0xB, the technique gauge (0x33, or 0x37 once it reaches 1000, cycling four frames) and, during the
    /// command turn, frame 10 around the partner view.</summary>
    /// <param name="canvas">The HUD drawing context.</param>
    /// <param name="view">The battle view; its active player selects the gauge frame.</param>
    private static void DrawHudFrames(BattleHudCanvas canvas, BattleView view)
    {
        canvas.Frame(0, 8, 0xF);
        canvas.Frame(1, 0xA1, 0xF);
        canvas.Frame(0x1E, 0x104, 0x3D);
        var player = view.Players.FirstOrDefault(fighter => fighter.Active);
        var gauge = Mathf.Clamp(player.BlastGauge, 0, 1000);
        var gaugeFrame = gauge == 1000 ? 0x37 : 0x33;
        canvas.Frame(gaugeFrame + (int)((canvas.Clock.Milliseconds / 16 / 4) & 3), 0x109, 0x3E);
        DrawGaugeFill(canvas.Canvas, gauge);
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
    /// <param name="hpDisplay">Persistent source HP-display state.</param>
    /// <param name="milliseconds">Presentation clock in milliseconds.</param>
    private static void DrawHpBar(CanvasItem canvas, int side, IReadOnlyList<BattleFighterView> fighters, BattleHudHpDisplay hpDisplay, ulong milliseconds)
    {
        var active = fighters.FirstOrDefault(fighter => fighter.Active);
        if (active.Id == 0)
        {
            return;
        }

        var points = side == 0 ? PlayerBar : EnemyBar;
        var height = points[2].Y - points[0].Y;
        var displayedHp = hpDisplay.Value(side, active, milliseconds);
        var (dark, light) = displayedHp > active.MaxHp / 4 ? (GreenDark, GreenLight) : (RedDark, RedLight);
        var filled = displayedHp > 0 && active.MaxHp > 0 ? Math.Max(3, (displayedHp << 7) / active.MaxHp) : 0;
        DrawHpGradient(canvas, side, points[0], height, filled, dark, light);
    }

    /// <summary>Draws the source's pixel gradient across the filled part of an HP bar.</summary>
    /// <param name="canvas">The HUD control inside its draw callback.</param>
    /// <param name="side">Zero for the player bar and one for the enemy bar.</param>
    /// <param name="origin">The source bar endpoint which remains fixed.</param>
    /// <param name="height">Bar height in source pixels.</param>
    /// <param name="filled">Filled width in source pixels.</param>
    /// <param name="dark">Source dark endpoint.</param>
    /// <param name="light">Source light endpoint.</param>
    private static void DrawHpGradient(CanvasItem canvas, int side, Vector2 origin, float height, int filled, Color dark, Color light)
    {
        for (var pixel = 0; pixel < filled; pixel++)
        {
            var colour = dark.Lerp(light, filled <= 1 ? 1f : (float)pixel / (filled - 1));
            var x = side == 0 ? origin.X + pixel : origin.X - pixel;
            canvas.DrawRect(new Rect2(x, origin.Y, 1, height), colour);
        }
    }

    /// <summary>Masks the empty portion of the 40-pixel technique gauge from its source 0..1000 value.</summary>
    /// <param name="canvas">The HUD control inside its draw callback.</param>
    /// <param name="gauge">The current technique gauge.</param>
    private static void DrawGaugeFill(CanvasItem canvas, int gauge)
    {
        var filled = gauge / 25;
        if (filled < 40)
        {
            canvas.DrawRect(new Rect2(0x109 + filled, 0x3E, 40 - filled, 8), Colors.Black);
        }
    }

    /// <summary>The player's current HP at 0x10B and maximum HP at 0x12E, both style-3 windows.</summary>
    /// <param name="canvas">The HUD drawing context.</param>
    /// <param name="view">The battle view; nothing is drawn without an active player.</param>
    /// <param name="hpDisplay">Persistent source HP-display state.</param>
    private static void DrawHpNumbers(BattleHudCanvas canvas, BattleView view, BattleHudHpDisplay hpDisplay)
    {
        var player = view.Players.FirstOrDefault(fighter => fighter.Active);
        if (player.Id != 0)
        {
            canvas.Text.DrawWindow(3, 0x10B, 0x1A, $"{hpDisplay.Value(0, player, canvas.Clock.Milliseconds)}", rightAlign: true, palette: BattleHudPalette.White);
            canvas.Text.DrawWindow(3, 0x12E, 0x1A, $"{player.MaxHp}", rightAlign: true, palette: BattleHudPalette.White);
        }
    }

    /// <summary>Draws the names of the active player and enemy fighters.</summary>
    /// <param name="canvas">The HUD drawing context.</param>
    /// <param name="view">The battle view of this frame.</param>
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

    /// <summary>Draws the status icons for one team.</summary>
    /// <param name="canvas">The HUD drawing context.</param>
    /// <param name="fighters">Fighters occupying the team's slots.</param>
    /// <param name="iconX">Source x coordinate for each slot.</param>
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
        int baseFrame = 0;
        if (fighter.KnockedOut)
        {
            baseFrame = 2;
        }
        else if (fighter.Hp != fighter.MaxHp)
        {
            baseFrame = 1;
        }

        return baseFrame + (fighter.Active ? 0xC : 0x3C);
    }

    /// <summary>The message window (frame 0x31 at (0xB, 0xBC)), its two lines at (0x14, 0xC2) and
    /// (0x14, 0xD0) as far as they are revealed, and once shown the continuation arrow whose CLUT row steps every arrow time step frames,
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
        if (view.MessageLines >= 1)
        {
            canvas.Text.DrawLine(1, 0x14, 0xC2, view.MessageText, view.Message, rightAlign: false, palette: BattleHudPalette.White);
        }

        if (view.MessageLines >= 2 && view.MessageText?.Second is { } second)
        {
            canvas.Text.DrawLine(1, 0x14, 0xD0, second, second.Debug, rightAlign: false, palette: BattleHudPalette.White);
        }

        if (view.MessageArrow && canvas.Sprites.MenuIcons is { } icons)
        {
            canvas.MenuIcon(icons.ArrowFrame, icons.ArrowX, icons.ArrowY, (int)(canvas.Clock.Milliseconds / 16UL / (ulong)icons.ArrowTimeStep) % 5);
        }
    }
}
