using Dw3Production.Diagnostics.Field;
using Dw3Production.Presentation;
using Dw3Production.Simulation;
using Godot;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>
/// Held-frame capture helpers of the sprite composition probe: one production frame presented without
/// advancing any effect, captured with every layer, then with the sprites, a model or the HUD hidden.
/// </summary>
internal sealed class BattleSpriteCompositionScene
{
    private readonly IBattleProbeHost _host;
    private readonly string _directory;

    /// <summary>Initializes a new instance of the <see cref="BattleSpriteCompositionScene"/> class.</summary>
    /// <param name="host">Offscreen production battle.</param>
    /// <param name="directory">Private capture directory.</param>
    public BattleSpriteCompositionScene(IBattleProbeHost host, string directory)
    {
        _host = host;
        _directory = directory;
        Directory.CreateDirectory(directory);
    }

    /// <summary>Gets the production sprite consumer.</summary>
    public BattleSpriteEffectPlayback Sprites => _host.Renderer.SequencePlayback.DiagnosticSprites ?? throw new InvalidOperationException("sprite consumer not configured");

    /// <summary>Gets the production sprite canvas.</summary>
    public BattleSpriteCanvas Canvas => Sprites.Surface as BattleSpriteCanvas ?? throw new InvalidOperationException("sprite surface is not the production canvas");

    /// <summary>Starts one effect through the production script consumer and advances it to a held sample.</summary>
    /// <param name="id">Sprite effect identity.</param>
    /// <param name="anchor">Script position: world point, or source screen point with a 0x7FFF or -1 depth.</param>
    /// <param name="steps">Physical steps advanced before the hold.</param>
    public void Start(int id, (short X, short Y, short Z) anchor, int steps)
    {
        SequenceAcknowledgement result = Sprites.Consume(new SpriteEffectEvent(BattleSide.Player, [0, (short)id, anchor.X, anchor.Y, anchor.Z]));
        ProbeRequirement.Require(result == SequenceAcknowledgement.Applied && Sprites.Active.Count == 1, $"effect {id} did not start: {result}");
        for (int step = 0; step < steps; step++)
        {
            Sprites.Step();
        }

        ProbeRequirement.Require(Sprites.Active.Count == 1, $"effect {id} ended before step {steps}");
    }

    /// <summary>Presents the held production frame and captures it.</summary>
    /// <param name="name">PNG name.</param>
    /// <returns>The capture.</returns>
    public async Task<Image> ShotAsync(string name)
    {
        _host.Renderer.Present(_host.BuildBattleView());
        await _host.NextFrameDrawn();
        return await BattleProbeCapture.CaptureRawAsync(_host, _directory, name);
    }

    /// <summary>Shows or hides the HUD for a model, stage and sprite-only comparison.</summary>
    /// <param name="visible">Whether the HUD draws.</param>
    public void SetHudVisible(bool visible) => _host.Renderer.SetHudVisibleForProbe(visible);

    /// <summary>Captures the held frame twice with nothing changed: every differing pixel is animation noise.</summary>
    /// <param name="name">PNG base name.</param>
    /// <returns>Pixels that changed between the two captures.</returns>
    public async Task<int> NoiseAsync(string name)
    {
        Image first = await ShotAsync(name + "-a.png");
        Image second = await ShotAsync(name + "-b.png");
        int changed = BattleCompositionMask.Changed(first, second).Count;
        GD.Print($"B9056C_NOISE {name} changed={changed}");
        return changed;
    }

    /// <summary>Captures the held frame with and without the sprites.</summary>
    /// <param name="name">PNG base name.</param>
    /// <returns>The sprite contribution.</returns>
    public async Task<BattleCompositionMask> SpritePairAsync(string name)
    {
        Image with = await ShotAsync(name + ".png");
        Canvas.SetVisibleForProbe(false, true);
        Image without = await ShotAsync(name + "-nosprites.png");
        Canvas.SetVisibleForProbe(true, true);
        return BattleCompositionMask.Changed(with, without);
    }

    /// <summary>Captures the held frame with and without one fighter model, the effects hidden.</summary>
    /// <param name="fighter">Fighter whose silhouette is measured.</param>
    /// <param name="name">PNG base name.</param>
    /// <returns>The model silhouette.</returns>
    public async Task<BattleCompositionMask> ModelMaskAsync(int fighter, string name)
    {
        Canvas.SetVisibleForProbe(false, true);
        Image with = await ShotAsync(name + ".png");
        _host.Renderer.SetModelVisibleForProbe(fighter, false);
        Image without = await ShotAsync(name + "-nomodel.png");
        _host.Renderer.SetModelVisibleForProbe(fighter, true);
        Canvas.SetVisibleForProbe(true, true);
        return BattleCompositionMask.Changed(with, without);
    }

    /// <summary>Captures the sprite footprint with one model hidden: what the effect draws over the stage alone.</summary>
    /// <param name="fighter">Hidden fighter.</param>
    /// <param name="name">PNG base name.</param>
    /// <returns>The unoccluded sprite footprint.</returns>
    public async Task<BattleCompositionMask> FootprintAsync(int fighter, string name)
    {
        _host.Renderer.SetModelVisibleForProbe(fighter, false);
        BattleCompositionMask footprint = await SpritePairAsync(name);
        _host.Renderer.SetModelVisibleForProbe(fighter, true);
        return footprint;
    }

    /// <summary>Captures the HUD's own pixels with every effect hidden.</summary>
    /// <param name="name">PNG base name.</param>
    /// <returns>The HUD pixels.</returns>
    public async Task<BattleCompositionMask> HudMaskAsync(string name)
    {
        Canvas.SetVisibleForProbe(false, true);
        Image with = await ShotAsync(name + ".png");
        _host.Renderer.SetHudVisibleForProbe(false);
        Image without = await ShotAsync(name + "-nohud.png");
        _host.Renderer.SetHudVisibleForProbe(true);
        Canvas.SetVisibleForProbe(true, true);
        return BattleCompositionMask.Changed(with, without);
    }
}
