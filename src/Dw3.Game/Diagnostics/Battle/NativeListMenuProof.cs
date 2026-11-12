using Dw3.Data;
using Dw3Production.Conversion;
using Dw3Production.Diagnostics.Field;
using Dw3Production.Presentation;
using Dw3Production.Simulation;
using Godot;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>Exercises real list consumers and painters in an isolated offscreen Godot scene.</summary>
public partial class NativeListMenuProof : Node
{
    /// <inheritdoc/>
    public override async void _Ready()
    {
        try
        {
            string packRoot = System.Environment.GetEnvironmentVariable("DW3_DATA_DIR") ?? throw new InvalidOperationException("Explicit prepared pack required.");
            string captures = System.Environment.GetEnvironmentVariable("DW3_CAPTURE_DIR") ?? throw new InvalidOperationException("Private capture directory required.");
            var pack = new DataPackReader(packRoot);
            using var loader = new BattleDataPackLoader(pack);
            using var dialogue = new DialogueDataPackLoader(pack);
            var data = new NativeListData(loader);
            using var capture = new NativeListCapture(this, loader, dialogue.LoadDialogue().Font, captures);
            await NativeListRefusalProof.NaturalTechniqueAsync(data, capture);
            await TechniqueAsync(data, capture);
            await NativeListRefusalProof.RunAsync(data, capture);
            await NativeListDigivolveProof.RunAsync(data, capture);
            GD.Print("NATIVE_LIST_MENU_PROOF PASS FIRST rendered-technique=real-consumer");
            GetTree().Quit(0);
        }
        catch (Exception error)
        {
            GD.PrintErr($"NATIVE_LIST_MENU_PROOF FAIL {error}");
            GetTree().Quit(1);
        }
    }

    /// <summary>Renders a seven-entry prepared pool, reaches its partial page and spends its technique once.</summary>
    /// <param name="data">Prepared catalogues with controlled learned state.</param>
    /// <param name="capture">Production HUD capture target.</param>
    /// <returns>Completion after the real command and render assertions.</returns>
    private static async Task TechniqueAsync(NativeListData data, NativeListCapture capture)
    {
        using var session = new NativeMenuSession(data.BuildFlow(), data.Text, data.Items);
        session.Tap(Key.Down);
        session.Tap(Key.Enter);
        ProbeRequirement.Require(session.Menu.Menu == BattleMenuKind.Technique && session.Menu.View.Lines.Count == 6, "FIRST real technique opener missing");
        using var first = await capture.SaveAsync(session, "technique-first");
        await NamesAsync(session, capture, first);
        session.Tap(Key.Pagedown);
        ProbeRequirement.Require(session.Menu.First == 6 && session.Menu.View.Lines.Count == 1, "FIRST partial last page missing");
        using var last = await capture.SaveAsync(session, "technique-last");
        await NamesAsync(session, capture, last);
        var selected = session.Menu.View.Lines[0];
        await capture.GlyphsAsync(last, selected.DescriptionText ?? throw new InvalidOperationException("Description missing."), new Vector2I(0x14, 0xC2), BattleHudPalette.White);
        await capture.CostAsync(last, data.Techniques[6].Tech.Mp);
        await capture.MpAsync(last, session.Player.Mp);
        int mp = session.Player.Mp;
        int hp = session.Enemy.Hp;
        session.KeyEvent(Key.Enter, true);
        ProbeRequirement.Require(session.Player.Mp == mp - selected.Cost, "FIRST technique debit not unique");
        ProbeRequirement.Require(session.Enemy.Hp < hp && session.Rng.Count > 0, "FIRST technique did not reach actual effect");
        string after = NativeMenuCommandsProof.Snapshot(session);
        session.KeyEvent(Key.Enter, true, echo: true);
        session.Tick();
        ProbeRequirement.Require(NativeMenuCommandsProof.Snapshot(session) == after, "FIRST held confirm repeated technique");
        await capture.SaveAsync(session, "technique-consumed");
        GD.Print($"NATIVE_LIST_CONSUMED tech={data.Techniques[6].Id} mp={mp}->{session.Player.Mp} hp={hp}->{session.Enemy.Hp} rng={session.Rng.Count}");
        NativeListRefusalProof.ReturnToPlayer(session);
        session.KeyEvent(Key.Enter, false);
        session.Tap(Key.X);
        session.Tap(Key.Down);
        session.Tap(Key.Enter);
        ProbeRequirement.Require(session.Menu.Menu == BattleMenuKind.Technique, "normal technique reentry missing");
        string beforeCancel = NativeMenuCommandsProof.Snapshot(session);
        session.Tap(Key.X);
        ProbeRequirement.Require(NativeMenuCommandsProof.Snapshot(session) == beforeCancel, "normal reentry cancellation spent resources");
    }

    /// <summary>Checks names actually drawn at all source row anchors.</summary>
    /// <param name="session">Production menu rows.</param>
    /// <param name="capture">Independent readback oracle.</param>
    /// <param name="image">Submitted real HUD pixels.</param>
    /// <returns>Completion after every populated row is checked.</returns>
    private static async Task NamesAsync(NativeMenuSession session, NativeListCapture capture, Image image)
    {
        for (int index = 0; index < session.Menu.View.Lines.Count; index++)
        {
            var line = session.Menu.View.Lines[index];
            var technique = session.Flow.PlayerTechniques[session.Menu.First + index];
            ProbeRequirement.Require(line.Cost == technique.Tech.Mp && line.Icon == technique.Tech.Icon + 0x37 && line.Text == technique.Text, "pack catalogue to row mismatch");
            await capture.GlyphsAsync(image, technique.Text ?? throw new InvalidOperationException("Technique name missing."), new Vector2I(0x2A, 0x45 + (index * 14)), line.Enabled ? BattleHudPalette.White : BattleHudPalette.Grey);
            await capture.IconAsync(image, technique.Tech.Icon + 0x37, index);
        }
    }
}
