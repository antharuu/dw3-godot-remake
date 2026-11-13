using Dw3Production.Diagnostics.Field;
using Dw3Production.Presentation;
using Godot;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>
/// Observes the production sprite consumer during one action: paired captures with and without the
/// sprite layers, the white flash at full level, the HUD digits the action changed and the destruction of every child.
/// </summary>
internal sealed class BattleSpriteEffectEvidence
{
    private const int CaptureInterval = 6;
    private const int MaxSpriteCaptures = 60;

    /// <summary>The source-screen row holding both status windows' HP and MP numbers.</summary>
    private static readonly Rect2I NumberRow = new(0, 20, 320, 18);

    private readonly IBattleProbeHost _host;
    private readonly string _directory;
    private readonly List<BattleSpriteEffectNode> _seen = [];
    private readonly HashSet<ulong> _instances = [];
    private readonly Dictionary<int, int> _bestPixels = [];
    private int _captures;
    private int _lastCaptureTick = -CaptureInterval;

    /// <summary>Initializes a new instance of the <see cref="BattleSpriteEffectEvidence"/> class.</summary>
    /// <param name="host">Offscreen production battle.</param>
    /// <param name="directory">Private capture directory of the case.</param>
    public BattleSpriteEffectEvidence(IBattleProbeHost host, string directory)
    {
        _host = host;
        _directory = directory;
        Directory.CreateDirectory(directory);
    }

    /// <summary>Gets the best changed-pixel count observed per started effect identity.</summary>
    public IReadOnlyDictionary<int, int> BestPixels => _bestPixels;

    /// <summary>Gets the distinct effect children observed, identical identities included.</summary>
    public IReadOnlyList<BattleSpriteEffectNode> Seen => _seen;

    /// <summary>Gets the peak white-flash measure, when the flash reached full white.</summary>
    public BattleSpritePixelDiff? FlashPeak { get; private set; }

    /// <summary>Gets the HUD pixels changed by the action's HP and MP values.</summary>
    public int DigitPixels { get; private set; }

    /// <summary>Gets the consumer under observation.</summary>
    private BattleSpriteEffectPlayback Sprites => _host.Renderer.SequencePlayback.DiagnosticSprites ?? throw new InvalidOperationException("sprite consumer not configured");

    /// <summary>Gets the production canvas.</summary>
    private BattleSpriteCanvas Canvas => Sprites.Surface as BattleSpriteCanvas ?? throw new InvalidOperationException("sprite surface is not the production canvas");

    /// <summary>Presents the unchanged view without advancing the battle until the HUD number row stops rolling.</summary>
    /// <param name="host">Offscreen production battle.</param>
    /// <returns>The settled capture.</returns>
    /// <exception cref="InvalidOperationException">The numbers never settled.</exception>
    public static async Task<Image> SettleAsync(IBattleProbeHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        Image previous = host.CaptureViewport();
        for (int frame = 0; frame < 600; frame++)
        {
            host.Renderer.Present(host.BuildBattleView(), 0);
            await host.NextFrameDrawn();
            Image current = host.CaptureViewport();
            if (frame > 30 && BattleSpritePixelDiff.Measure(Row(current), Row(previous)).Changed == 0)
            {
                return current;
            }

            previous = current;
        }

        throw new InvalidOperationException("HUD numbers never settled");
    }

    /// <summary>Records new children and takes the paired captures due at this tick.</summary>
    /// <param name="tick">Production runner tick.</param>
    /// <returns>Completion after any capture.</returns>
    public async Task ObserveAsync(int tick)
    {
        bool started = false;
        foreach (BattleSpriteEffectNode node in Canvas.Nodes.Where(node => _instances.Add(node.GetInstanceId())))
        {
            started = true;
            _seen.Add(node);
            GD.Print($"B9056_CHILD tick={tick} name={node.Name} id={node.Instance.Id} serial={node.Instance.Serial} layer={node.Instance.Layer} anchor={node.Instance.Anchor} animations={node.Instance.Sheet.Animations.Count}");
        }

        if (Sprites.Active.Count > 0 && _captures < MaxSpriteCaptures && (started || tick - _lastCaptureTick >= CaptureInterval))
        {
            _lastCaptureTick = tick;
            await CaptureSpritesAsync(tick);
        }

        if (FlashPeak is null && Sprites.FlashLevel == BattleWhiteFlash.FullLevel)
        {
            FlashPeak = await PairAsync($"flash-peak-t{tick}", sprites: true);
            GD.Print($"B9056_FLASH tick={tick} level={Sprites.FlashLevel} changed={FlashPeak.Value.Changed} white={FlashPeak.Value.White}/{FlashPeak.Value.Total}");
        }
    }

    /// <summary>
    /// Lets the HUD's rolling HP and MP numbers settle on the action's result, then compares the HUD number
    /// row with the capture taken before the action: the difference is the digits the action changed.
    /// </summary>
    /// <param name="before">Settled capture taken before the action.</param>
    /// <returns>Changed pixels in the number row.</returns>
    public async Task<int> CaptureHudDigitsAsync(Image before)
    {
        ArgumentNullException.ThrowIfNull(before);
        Image settled = await SettleAsync(_host);
        settled.SavePng(Path.Combine(_directory, "hud-digits-settled.png"));
        Rect2I region = RowRegion();
        using Image after = Row(settled);
        using Image previous = Row(before);
        after.SavePng(Path.Combine(_directory, "hud-digits-after-row.png"));
        previous.SavePng(Path.Combine(_directory, "hud-digits-before-row.png"));
        BattleSpritePixelDiff diff = BattleSpritePixelDiff.Measure(after, previous);
        DigitPixels = diff.Changed;
        BattleView? view = _host.BuildBattleView();
        string values = view is { } shown ? string.Join(' ', shown.Players.Concat(shown.Enemies).Select(fighter => $"{fighter.Id}:{fighter.Hp}/{fighter.Mp}")) : "none";
        GD.Print($"B9056_DIGITS region={region} changed={diff.Changed} values=[{values}]");
        return diff.Changed;
    }

    /// <summary>Requires that every observed child was hidden, queued and then destroyed by the production frame.</summary>
    /// <param name="label">Normal or cancelled lifecycle.</param>
    /// <returns>Completion once destruction is verified.</returns>
    public async Task VerifyReleasedAsync(string label)
    {
        ProbeRequirement.Require(Sprites.Active.Count == 0 && Canvas.Nodes.Count == 0 && Sprites.StartedCount == 0, $"{label}: sprite consumer kept children");
        ProbeRequirement.Require(_seen.All(node => !GodotObject.IsInstanceValid(node) || (!node.Visible && node.IsQueuedForDeletion())), $"{label}: live child not queued");
        ProbeRequirement.Require(!Canvas.Flash.Visible && Sprites.Flashes.Count == 0, $"{label}: white flash survived");
        await _host.NextFrameDrawn();
        ProbeRequirement.Require(_seen.All(node => !GodotObject.IsInstanceValid(node)), $"{label}: child survived the production frame");
        GD.Print($"B9056_RELEASED {label} children={_seen.Count} invalid={_seen.Count(node => !GodotObject.IsInstanceValid(node))} flash=hidden");
    }

    /// <summary>Gets the HUD number row in viewport pixels.</summary>
    /// <returns>The row rectangle.</returns>
    private static Rect2I RowRegion()
    {
        (int x, int y) = BattleHudLayout.ToViewport(NumberRow.Position.X, NumberRow.Position.Y);
        (int width, int height) = BattleHudLayout.ToViewportSize(NumberRow.Size.X, NumberRow.Size.Y);
        return new Rect2I(x, y, width, height);
    }

    /// <summary>Crops the HUD number row.</summary>
    /// <param name="image">Full capture.</param>
    /// <returns>The row image.</returns>
    private static Image Row(Image image) => image.GetRegion(RowRegion());

    /// <summary>Takes one with/without pair and credits its pixels to every active identity.</summary>
    /// <param name="tick">Production runner tick.</param>
    /// <returns>Completion after both captures.</returns>
    private async Task CaptureSpritesAsync(int tick)
    {
        _captures++;
        var active = Sprites.Active.Select(instance => $"{instance.Id}#{instance.Serial}@[{string.Join(',', instance.Times)}]").ToArray();
        int passes = Canvas.Nodes.Sum(node => node.DrawnPasses);
        BattleSpritePixelDiff diff = await PairAsync($"sprite-t{tick:D4}", sprites: false);
        foreach (int id in Sprites.Active.Select(instance => instance.Id))
        {
            _bestPixels[id] = Math.Max(_bestPixels.GetValueOrDefault(id), diff.Changed);
        }

        GD.Print($"B9056_SPRITES tick={tick} active={string.Join(' ', active)} passes={passes} flash={Sprites.FlashLevel} changed={diff.Changed} box=({diff.MinX},{diff.MinY})-({diff.MaxX},{diff.MaxY})");
    }

    /// <summary>Captures the current frame with everything, then with either the sprite layers or the flash hidden.</summary>
    /// <param name="name">Capture base name.</param>
    /// <param name="sprites">Whether the sprites stay drawn in the second capture (true isolates the flash).</param>
    /// <returns>The measured contribution of the hidden part.</returns>
    private async Task<BattleSpritePixelDiff> PairAsync(string name, bool sprites)
    {
        Image with = await BattleProbeCapture.CaptureRawAsync(_host, _directory, name + ".png");
        Canvas.SetVisibleForProbe(sprites, !sprites);
        Image without = await BattleProbeCapture.CaptureRawAsync(_host, _directory, name + (sprites ? "-noflash.png" : "-nosprites.png"));
        Canvas.SetVisibleForProbe(true, true);
        return BattleSpritePixelDiff.Measure(with, without);
    }
}
