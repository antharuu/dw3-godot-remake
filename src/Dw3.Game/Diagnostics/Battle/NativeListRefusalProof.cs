using Dw3Production.Diagnostics.Field;
using Dw3Production.Presentation;
using Dw3Production.Simulation;
using Godot;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>Checks list refusal, cancellation and empty panes through native host gestures.</summary>
internal static class NativeListRefusalProof
{
    /// <summary>Exercises unaffordable, prohibited and empty lists without command or RNG costs.</summary>
    /// <param name="data">Prepared catalogue data.</param>
    /// <param name="capture">Production renderer and readback assertions.</param>
    /// <returns>Completion after all rendered refusal cases.</returns>
    public static async Task RunAsync(NativeListData data, NativeListCapture capture)
    {
        using var session = new NativeMenuSession(data.BuildFlow(), data.Text, data.Items);
        session.Player.Mp = 0;
        session.Tap(Key.Down);
        session.Tap(Key.Enter);
        ProbeRequirement.Require(!session.Menu.CanConfirm, "unaffordable technique is enabled");
        using var image = await capture.SaveAsync(session, "technique-refused");
        await capture.GlyphsAsync(image, data.Techniques[0].Text ?? throw new InvalidOperationException("Technique name missing."), new Vector2I(42, 69), BattleHudPalette.Grey);
        await capture.GlyphsAsync(image, session.Text.MenuEntry(0x54), new Vector2I(20, 194), BattleHudPalette.Grey);
        string before = NativeMenuCommandsProof.Snapshot(session);
        session.Tap(Key.Enter);
        session.Tap(Key.X);
        ProbeRequirement.Require(NativeMenuCommandsProof.Snapshot(session) == before && session.Menu.Menu == BattleMenuKind.Root, "refused technique or cancellation spent resources");
        session.Player.Flags |= FighterFlags.Confused;
        session.Menu.OpenRoot(session.Flow.RootMenuLines());
        session.Tap(Key.Down);
        session.Tap(Key.Enter);
        ProbeRequirement.Require(session.Menu.Menu == BattleMenuKind.Root && NativeMenuCommandsProof.Snapshot(session) == before, "prohibited technique reached consumer");
        session.Player.Flags = 0;
        await EmptyAsync(session, capture);
        GD.Print("NATIVE_LIST_REFUSAL PASS unaffordable-grey prohibited no-mp-turn-item-rng-cost empty-technique empty-digivolve");
    }

    /// <summary>Consumes the natural current form's actual session-filtered technique catalogue.</summary>
    /// <param name="data">Prepared data.</param>
    /// <param name="capture">Production painter.</param>
    /// <returns>Completion after the actual command.</returns>
    public static async Task NaturalTechniqueAsync(NativeListData data, NativeListCapture capture)
    {
        using var session = new NativeMenuSession(data.BuildFlow(pagedPool: false), data.Text, data.Items);
        session.Tap(Key.Down);
        session.Tap(Key.Enter);
        var technique = session.Flow.PlayerTechniques[0];
        using var image = await capture.SaveAsync(session, "natural-technique");
        await capture.GlyphsAsync(image, technique.Text ?? throw new InvalidOperationException("Technique name missing."), new Vector2I(42, 69), BattleHudPalette.White);
        await capture.CostAsync(image, technique.Tech.Mp);
        int mp = session.Player.Mp;
        int hp = session.Enemy.Hp;
        session.Tap(Key.Enter);
        ProbeRequirement.Require(session.Player.Mp == mp - technique.Tech.Mp && session.Enemy.Hp < hp, "natural session catalogue command not consumed");
        GD.Print($"NATIVE_LIST_NATURAL PASS form={data.Partner.OriginFormId} technique={technique.Id} mp={mp}->{session.Player.Mp}");
    }

    /// <summary>Completes fixture lifecycle only, never choosing the player's UI command directly.</summary>
    /// <param name="session">Already-consumed native battle.</param>
    public static void ReturnToPlayer(NativeMenuSession session)
    {
        for (int step = 0; step < 32 && session.Flow.Phase != BattleFlowPhase.PlayerCommand; step++)
        {
            if (session.Flow.Phase == BattleFlowPhase.ActionSequence)
            {
                session.Flow.CompleteActionSequence();
            }
            else
            {
                session.Flow.EnemyAct(session.Rng);
            }
        }

        ProbeRequirement.Require(session.Flow.Phase == BattleFlowPhase.PlayerCommand, "fixture scheduler did not return player turn");
    }

    /// <summary>Opens the real empty panes and returns through native cancellation.</summary>
    /// <param name="session">Native host.</param>
    /// <param name="capture">Submitted renderer.</param>
    /// <returns>Completion after captures.</returns>
    private static async Task EmptyAsync(NativeMenuSession session, NativeListCapture capture)
    {
        session.Flow.PlayerTechniques = [];
        session.Flow.PlayerDigivolutions = [];
        session.Menu.OpenRoot(session.Flow.RootMenuLines());
        for (int row = 1; row <= 2; row++)
        {
            NativeMenuCommandsProof.Select(session, row);
            session.Tap(Key.Enter);
            ProbeRequirement.Require(session.Menu.Lines.Count == 0 && session.Menu.Menu != BattleMenuKind.Root, "empty pane missing");
            await capture.SaveAsync(session, $"empty-{session.Menu.Menu}");
            string before = NativeMenuCommandsProof.Snapshot(session);
            session.Tap(Key.Enter);
            session.Tap(Key.Down);
            session.Tap(Key.Pagedown);
            session.Tap(Key.X);
            ProbeRequirement.Require(NativeMenuCommandsProof.Snapshot(session) == before && session.Menu.Menu == BattleMenuKind.Root, "empty list consumed resources");
        }
    }
}
