using Dw3Production.Conversion;
using Dw3Production.Diagnostics.Field;
using Dw3Production.Logging;
using Dw3Production.Simulation;
using Godot;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>Proves battle text uses exact decoded CLUT colors without renderer tint.</summary>
internal sealed class BattleFontProbe : BattleProbe
{
    /// <summary>Initializes a new instance of the <see cref="BattleFontProbe"/> class.</summary>
    /// <param name="host">Restricted live battle host.</param>
    public BattleFontProbe(IBattleProbeHost host)
        : base(host)
    {
    }

    /// <inheritdoc/>
    protected override string FailureCode => "BATTLE_FONT_PROBE";

    /// <inheritdoc/>
    protected override async Task ExecuteAsync()
    {
        var captureRoot = Host.PrepareCaptureDirectory("probe/battle-font");
        var font = Host.Dialogue?.Font ?? throw new InvalidOperationException("font unavailable");
        ProbeRequirement.Require(
            font.Styles.Count == 3
            && font.Style(1).LineHeight == 14
            && font.Style(2).LineHeight == 11
            && font.Style(3).LineHeight == 9,
            "font styles must be exactly 14/11/9");
        Host.StartDebugBattle();
        ProbeRequirement.Require(Host.HasActiveBattle, "F8 did not create battle HUD");
        var white = BattleFontPixels.Palette(font, 1, 0);
        var darkBlue = BattleFontPixels.Palette(font, 3, 2);
        var grey = BattleFontPixels.Palette(font, 1, 7);
        var rootWhite = await AssertRootWhiteAsync(captureRoot, white);
        var disabledGrey = await AssertDisabledGreyAsync(captureRoot, grey);
        var darkBlueButtons = await AssertTechniqueButtonsAsync(captureRoot, darkBlue);
        var knockedOutGrey = await AssertKnockedOutNameAsync(captureRoot, grey);
        QuietLog.Info($"battle-font CLUT white_pixels={rootWhite} grey_disabled={disabledGrey} grey_ko={knockedOutGrey} dark_blue_buttons={darkBlueButtons} white_rgb={string.Join(',', white.First())} grey_rgb={string.Join(',', grey.First())} dark_blue_rgb={string.Join(',', darkBlue.First())}");
        QuietLog.Info($"battle-font probe PASS captures={captureRoot}/hud-numbers.png,{captureRoot}/command-disabled.png,{captureRoot}/technique-page-buttons.png,{captureRoot}/switch-ko.png");
    }

    /// <summary>Maps a source screen coordinate to the 1280x720 viewport.</summary>
    /// <param name="x">Source x coordinate.</param>
    /// <param name="y">Source y coordinate.</param>
    /// <returns>The viewport pixel position.</returns>
    private static Vector2 SourcePosition(float x, float y) => new((x * 3f) + 160f, y * 3f);

    /// <summary>Checks enabled root-command glyphs against the white source palette.</summary>
    /// <param name="captureRoot">Directory receiving the capture.</param>
    /// <param name="colors">Exact white palette colors.</param>
    /// <returns>The matching pixel count.</returns>
    private async Task<int> AssertRootWhiteAsync(string captureRoot, HashSet<(byte R, byte G, byte B)> colors)
    {
        var root = await BattleProbeCapture.CaptureAsync(Host, captureRoot, "hud-numbers.png");
        var count = BattleFontPixels.Count(root, new Rect2(SourcePosition(36, 109), new Vector2(360, 342)), colors);
        ProbeRequirement.Require(count > 20, $"root command glyphs lack WHITE CLUT pixels count={count}");
        return count;
    }

    /// <summary>Checks sleeping commands against the grey source palette.</summary>
    /// <param name="captureRoot">Directory receiving the capture.</param>
    /// <param name="colors">Exact grey palette colors.</param>
    /// <returns>The matching pixel count.</returns>
    private async Task<int> AssertDisabledGreyAsync(string captureRoot, HashSet<(byte R, byte G, byte B)> colors)
    {
        Host.Flow.State.ActivePlayerFighter!.Flags |= FighterFlags.Asleep;
        Host.Menu.OpenRoot(Host.Flow.RootMenuLines());
        var disabled = await BattleProbeCapture.CaptureAsync(Host, captureRoot, "command-disabled.png");
        var count = BattleFontPixels.Count(disabled, new Rect2(SourcePosition(36, 109), new Vector2(360, 57)), colors);
        ProbeRequirement.Require(count > 20, $"disabled command glyphs lack GREY CLUT pixels count={count}");
        return count;
    }

    /// <summary>Checks both technique page labels against the dark-blue source palette.</summary>
    /// <param name="captureRoot">Directory receiving the capture.</param>
    /// <param name="colors">Exact dark-blue palette colors.</param>
    /// <returns>The matching pixel count.</returns>
    private async Task<int> AssertTechniqueButtonsAsync(string captureRoot, HashSet<(byte R, byte G, byte B)> colors)
    {
        Host.OpenSubmenu(
            BattleMenuKind.Technique,
            Host.Flow.PlayerTechniques.Select(technique => (technique.Id, technique.Text ?? BattleTextLine.Missing)).ToList(),
            line: (id, text) => new BattleMenuLine(text.Debug, true, text));
        var technique = await BattleProbeCapture.CaptureAsync(Host, captureRoot, "technique-page-buttons.png");
        var count = BattleFontPixels.Count(technique, new Rect2(SourcePosition(26, 159), new Vector2(330, 30)), colors);
        ProbeRequirement.Require(count > 8, $"technique page buttons lack DARK_BLUE CLUT pixels count={count}");
        return count;
    }

    /// <summary>Checks a knocked-out switch candidate against the grey source palette.</summary>
    /// <param name="captureRoot">Directory receiving the capture.</param>
    /// <param name="colors">Exact grey palette colors.</param>
    /// <returns>The matching pixel count.</returns>
    private async Task<int> AssertKnockedOutNameAsync(string captureRoot, HashSet<(byte R, byte G, byte B)> colors)
    {
        var knockedOut = Host.Flow.State.Players.Select((player, index) => (player, index))
            .First(pair => pair.player is not null && pair.index != Host.Flow.State.ActivePlayer).player!;
        knockedOut.Hp = 0;
        Host.OpenSubmenu(
            BattleMenuKind.Switch,
            Host.SwitchLines(Host.Flow),
            slot => !Host.Flow.State.Players[slot]!.IsKnockedOut);
        var change = await BattleProbeCapture.CaptureAsync(Host, captureRoot, "switch-ko.png");
        var count = BattleFontPixels.Count(change, new Rect2(SourcePosition(36, 76), new Vector2(360, 42)), colors);
        ProbeRequirement.Require(count > 8, $"KO switch name lacks GREY CLUT pixels count={count}");
        return count;
    }
}
