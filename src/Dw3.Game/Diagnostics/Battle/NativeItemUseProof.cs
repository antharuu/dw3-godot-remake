using Dw3Production.Diagnostics.Field;
using Dw3Production.Simulation;
using Godot;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>Exercises successful supported item effects through the native host input route.</summary>
internal static class NativeItemUseProof
{
    /// <summary>Uses fabricated source-shaped healing and poison-cure items through root and item menus.</summary>
    public static void ApplyHealingAndStatusItems()
    {
        VerifyHealingItem();
        VerifyPoisonCure();
        GD.Print("NATIVE_ROOT_MENU_PROOF PASS items=heal-poison-cure ingress=GameHost._Input stock=decremented");
    }

    /// <summary>Reaches a healing item through the production root and menu driver, then observes HP and stock.</summary>
    private static void VerifyHealingItem()
    {
        using var session = new NativeMenuSession();
        session.Player.Hp = 50;
        OpenItemMenu(session);
        session.Tap(Key.Enter);
        ProbeRequirement.Require(session.Player.Hp == 100 && session.Flow.ItemStock[0x2B] == 1, "item healing did not apply through native menu");
    }

    /// <summary>Reaches the poison cure through the production item page, then observes the cleared established status and stock.</summary>
    private static void VerifyPoisonCure()
    {
        using var session = new NativeMenuSession();
        session.Player.Flags |= FighterFlags.Poisoned;
        OpenItemMenu(session);
        session.Tap(Key.Down);
        session.Tap(Key.Enter);
        ProbeRequirement.Require((session.Player.Flags & FighterFlags.Poisoned) == 0 && session.Flow.ItemStock[0x42] == 1, "item poison cure did not apply through native menu");
    }

    /// <summary>Opens the real item page from the Item root row with native key gestures.</summary>
    /// <param name="session">Native host session to navigate.</param>
    private static void OpenItemMenu(NativeMenuSession session)
    {
        NativeMenuCommandsProof.Select(session, 4);
        session.Tap(Key.Enter);
        ProbeRequirement.Require(session.Menu.Menu == BattleMenuKind.Item && session.Menu.Lines.Count == 2, "item menu did not open through native root input");
    }
}
