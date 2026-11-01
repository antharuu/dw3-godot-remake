using Xunit;

namespace Dw3.Game.Tests;

using Dw3Production.Presentation;

public sealed class LegacyDepthOrderConsoleTests
{

    [Fact]
    public void PreservesLegacyConsoleAssertions()
    {
        PreservesOrderingTableDirection();
        CoversTerrainAndPlayerOverlap();
        OrdersCallbacksByGroundYAndRegistration();
    }

    private static void PreservesOrderingTableDirection()
    {
        Check(SourceDepth.ToGodotZIndex(11) < SourceDepth.ToGodotZIndex(7), "source depth 11 draws before depth 7");
        Check(SourceDepth.ToGodotZIndex(7) < SourceDepth.ToGodotZIndex(3), "source depth 7 draws before depth 3");
    }

    private static void CoversTerrainAndPlayerOverlap()
    {
        var player = SourceDepth.ToGodotZIndex(4);
        Check(SourceDepth.ToGodotZIndex(7) < player, "depth-0 terrain stays behind player depth 4");
        Check(player < SourceDepth.ToGodotZIndex(3), "player depth 4 stays behind depth-1 terrain");
        Check(SourceDepth.ToGodotZIndex(11) < player, "depth-2 terrain stays behind player depth 4");
    }

    private static void OrdersCallbacksByGroundYAndRegistration()
    {
        var ordered = SourceCallbackOrder.Order(
        [
            (Item: "water-behind", Priority: 246, Registration: SourceCallbackOrder.MapObjectRegistration),
            (Item: "player", Priority: 246, Registration: SourceCallbackOrder.PlayerRegistration),
            (Item: "water-front", Priority: 247, Registration: SourceCallbackOrder.MapObjectRegistration),
            (Item: "far-water", Priority: 245, Registration: SourceCallbackOrder.MapObjectRegistration),
        ]);
        var order = ordered.ToArray();
        Check(order.SequenceEqual(["far-water", "player", "water-behind", "water-front"]),
            "callback painter order must be proportional Y with later actor registration first on an equal key");
        Check(Array.IndexOf(order, "water-behind") < Array.IndexOf(order, "water-front"),
            "a lower sortY must draw before a higher sortY");
        Check(Array.IndexOf(order, "player") < Array.IndexOf(order, "water-behind"),
            "equal player/object sort key must preserve reversed stable callback insertion");
    }

    private static void Check(bool condition, string description)
    {
        Assert.True(condition, description);
    }
}
