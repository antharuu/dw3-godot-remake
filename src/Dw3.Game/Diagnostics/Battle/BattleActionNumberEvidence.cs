using Dw3Production.Diagnostics.Field;
using Dw3Production.Presentation;
using Dw3Production.Simulation;
using Godot;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>
/// Observes the action-result message through the production runner and renderer: the page built from the
/// frozen rule result, its line-by-line reveal captured on each line, the glyphs of the number line, the arrow,
/// and the box closed once the action completed.
/// </summary>
internal sealed class BattleActionNumberEvidence
{
    /// <summary>The second message line, which prints the numbers: text at (0x14, 0xD0), one 14-pixel style-1 row.</summary>
    private static readonly Rect2I NumberLine = new(0x14, 0xD0, 0x110, 14);

    private readonly IBattleProbeHost _host;
    private readonly string _directory;
    private Image? _firstLine;
    private int _lines;

    /// <summary>Initializes a new instance of the <see cref="BattleActionNumberEvidence"/> class.</summary>
    /// <param name="host">Offscreen production battle.</param>
    /// <param name="directory">Private capture directory of the case.</param>
    public BattleActionNumberEvidence(IBattleProbeHost host, string directory)
    {
        _host = host;
        _directory = directory;
    }

    /// <summary>Gets the first result page shown, or null when none was shown.</summary>
    public BattleActionResultPage? Page { get; private set; }

    /// <summary>Gets the pixels the number line added to the message box when it was revealed.</summary>
    public int NumberPixels { get; private set; }

    /// <summary>Gets the tick the result message appeared, or -1.</summary>
    public int ShownTick { get; private set; } = -1;

    /// <summary>Gets the tick the arrow appeared, or -1.</summary>
    public int ArrowTick { get; private set; } = -1;

    /// <summary>Records the result message state of the frame just drawn, capturing each newly revealed line.</summary>
    /// <param name="tick">Production runner tick.</param>
    /// <returns>Completion after any capture.</returns>
    public async Task ObserveAsync(int tick)
    {
        if (_host.Flow.ActionResult is not { } result)
        {
            return;
        }

        if (Page is null)
        {
            Page = result.Page;
            ShownTick = tick;
            GD.Print($"B9056_NUMBER_SHOWN tick={tick} key={Page.Key} target_side={Page.TargetSide} target={Page.TargetId} numbers=[{string.Join(',', Page.Numbers)}] line0={_host.Flow.MessageText.Debug} line1={_host.Flow.MessageText.Second?.Debug}");
        }

        await CaptureRevealAsync(tick, result.Reveal);
    }

    /// <summary>Requires the shown page to match the rule result: its target, amount and the HP that target lost.</summary>
    /// <param name="context">Frozen context of the action.</param>
    /// <param name="hpBefore">Target HP before the command resolved.</param>
    /// <param name="hpAfter">Target HP after the rules applied the hit.</param>
    public void RequireRuleResult(ActionSequenceContext context, int hpBefore, int hpAfter)
    {
        ArgumentNullException.ThrowIfNull(context);
        BattleActionResultPage page = Page ?? throw new InvalidOperationException("no result message was shown");
        int formula = context.HitEvents.Where(hit => !hit.IsCounter && hit.Landed).Sum(hit => hit.FormulaDamage);
        int lost = hpBefore - hpAfter;
        GD.Print($"B9056_NUMBER_RULE target={context.TargetId} page_target={page.TargetId} amount={page.Amount} formula={formula} hp={hpBefore}->{hpAfter} lost={lost} number_pixels={NumberPixels} shown={ShownTick} arrow={ArrowTick} closed={!_host.Flow.MessageVisible || _host.Flow.ActionResult is null}");
        ProbeRequirement.Require(page.TargetId == context.TargetId, "result names another fighter than the action's target");
        ProbeRequirement.Require(page.Key != "damage" || (page.Amount == formula && (lost == page.Amount || hpAfter == 0)), "printed damage differs from the rule result");
        ProbeRequirement.Require(page.Key == "missed" || NumberPixels > 0, "the number line drew no glyph");
        ProbeRequirement.Require(ArrowTick > ShownTick && _host.Flow.ActionResult is null, "result message did not reveal its arrow and close");
    }

    /// <summary>Gets the number line in viewport pixels.</summary>
    /// <returns>The rectangle.</returns>
    private static Rect2I Region()
    {
        (int x, int y) = BattleHudLayout.ToViewport(NumberLine.Position.X, NumberLine.Position.Y);
        (int width, int height) = BattleHudLayout.ToViewportSize(NumberLine.Size.X, NumberLine.Size.Y);
        return new Rect2I(x, y, width, height);
    }

    /// <summary>Captures the frame each time one more line is revealed, and once the arrow shows.</summary>
    /// <param name="tick">Production runner tick.</param>
    /// <param name="reveal">Current page reveal.</param>
    /// <returns>Completion after any capture.</returns>
    private async Task CaptureRevealAsync(int tick, BattleMessageReveal reveal)
    {
        if (reveal.VisibleLines > _lines)
        {
            _lines = reveal.VisibleLines;
            Image image = await BattleProbeCapture.CaptureRawAsync(_host, _directory, $"number-line{_lines}-t{tick:D4}.png");
            MeasureNumberLine(image);
        }

        if (reveal.Arrow && ArrowTick < 0)
        {
            ArrowTick = tick;
            await BattleProbeCapture.CaptureRawAsync(_host, _directory, $"number-arrow-t{tick:D4}.png");
            GD.Print($"B9056_NUMBER_ARROW tick={tick}");
        }
    }

    /// <summary>Keeps the one-line capture, then measures what the second line added to the number row.</summary>
    /// <param name="image">Capture after a reveal.</param>
    private void MeasureNumberLine(Image image)
    {
        if (_lines == 1)
        {
            _firstLine = image;
            return;
        }

        if (_firstLine is null)
        {
            return;
        }

        using Image before = _firstLine.GetRegion(Region());
        using Image after = image.GetRegion(Region());
        NumberPixels = BattleSpritePixelDiff.Measure(after, before).Changed;
        GD.Print($"B9056_NUMBER_LINE region={Region()} pixels={NumberPixels}");
    }
}
