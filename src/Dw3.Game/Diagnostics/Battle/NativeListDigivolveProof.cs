using Dw3Production.Diagnostics.Field;
using Dw3Production.Presentation;
using Dw3Production.Simulation;
using Godot;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>Checks the equipped-form list and its real transformation consumer, separate from physical playback.</summary>
internal static class NativeListDigivolveProof
{
    /// <summary>Renders the current form and all equipped slots, refuses current and transforms from the last row.</summary>
    /// <param name="data">Prepared forms and explicit acquired/equipped state.</param>
    /// <param name="capture">Submitted production painter.</param>
    /// <returns>Completion after native navigation and real state assertions.</returns>
    public static async Task RunAsync(NativeListData data, NativeListCapture capture)
    {
        using var session = new NativeMenuSession(data.BuildFlow(pagedPool: false), data.Text, data.Items);
        Open(session);
        ProbeRequirement.Require(session.Menu.Lines.Count == 4 && !session.Menu.Lines[0].Enabled, $"base plus three equipped forms missing current={session.Player.Id} origin={data.Partner.OriginFormId} rows={string.Join(',', session.Flow.PlayerDigivolutions)} enabled={session.Menu.Lines[0].Enabled}");
        ProbeRequirement.Require(data.Partner.KnownForms.Count == 5 && !session.Flow.PlayerDigivolutions.Contains(data.Partner.KnownForms[4]), "acquired unequipped form leaked into battle list");
        using var image = await capture.SaveAsync(session, "digivolve-equipped");
        await NamesAsync(session, capture, image);
        string before = NativeMenuCommandsProof.Snapshot(session);
        session.Tap(Key.Enter);
        ProbeRequirement.Require(NativeMenuCommandsProof.Snapshot(session) == before, "current form refusal spent resources");
        session.Tap(Key.Down);
        session.Tap(Key.Down);
        session.Tap(Key.Down);
        using var last = await capture.SaveAsync(session, "digivolve-last-slot");
        int mp = session.Player.Mp;
        int target = session.Flow.PlayerDigivolutions[3];
        session.KeyEvent(Key.Enter, true);
        ProbeRequirement.Require(session.Player.Id == target && session.Player.Mp == mp, "last equipped form did not become active without MP debit");
        string consumed = NativeMenuCommandsProof.Snapshot(session);
        session.KeyEvent(Key.Enter, true, echo: true);
        session.Tick();
        ProbeRequirement.Require(NativeMenuCommandsProof.Snapshot(session) == consumed, "held digivolve repeated");
        NativeListRefusalProof.ReturnToPlayer(session);
        session.KeyEvent(Key.Enter, false);
        session.Tap(Key.X);
        Open(session);
        ProbeRequirement.Require(!session.Menu.Lines[3].Enabled && session.Menu.Lines[0].Enabled, "current/equipped rows stale after normal reentry");
        using var reentry = await capture.SaveAsync(session, "digivolve-reentry");
        await NamesAsync(session, capture, reentry);
        before = NativeMenuCommandsProof.Snapshot(session);
        session.Tap(Key.X);
        ProbeRequirement.Require(NativeMenuCommandsProof.Snapshot(session) == before, "digivolve cancellation spent resources");
        session.Player.Flags |= FighterFlags.NoDigivolve;
        session.Menu.OpenRoot(session.Flow.RootMenuLines());
        before = NativeMenuCommandsProof.Snapshot(session);
        session.Tap(Key.Down);
        session.Tap(Key.Down);
        session.Tap(Key.Enter);
        ProbeRequirement.Require(session.Menu.Menu == BattleMenuKind.Root && NativeMenuCommandsProof.Snapshot(session) == before, "prohibited digivolve spent resources");
        await capture.SaveAsync(session, "digivolve-prohibited");
        GD.Print($"NATIVE_LIST_DIGIVOLVE PASS known={string.Join(',', data.Partner.KnownForms)} equipped={string.Join(',', data.Partner.EquippedForms)} active={target} mp={mp} current-refused reentry cancel-no-cost");
    }

    /// <summary>Opens the real form page through native root gestures.</summary>
    /// <param name="session">Native host.</param>
    private static void Open(NativeMenuSession session)
    {
        session.Tap(Key.Down);
        session.Tap(Key.Down);
        session.Tap(Key.Enter);
        ProbeRequirement.Require(session.Menu.Menu == BattleMenuKind.Digivolve, "production digivolve opener missing");
    }

    /// <summary>Checks source name tokens actually rendered at each form row.</summary>
    /// <param name="session">Production form menu.</param>
    /// <param name="capture">Pixel oracle.</param>
    /// <param name="image">Submitted real pixels.</param>
    /// <returns>Completion after each expected name and palette.</returns>
    private static async Task NamesAsync(NativeMenuSession session, NativeListCapture capture, Image image)
    {
        for (int row = 0; row < session.Menu.Lines.Count; row++)
        {
            int id = session.Flow.PlayerDigivolutions[row];
            var expected = session.Text.DigimonByNameId(session.Flow.NameIds[id]);
            await capture.GlyphsAsync(image, expected, new Vector2I(0xBB, 0x92 + (row * 19)), session.Menu.Lines[row].Enabled ? BattleHudPalette.White : BattleHudPalette.Grey);
        }
    }
}
