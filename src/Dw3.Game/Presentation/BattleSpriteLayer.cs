namespace Dw3Production.Presentation;

/// <summary>The source battle layer a sprite effect draws on.</summary>
internal enum BattleSpriteLayer
{
    /// <summary>The models layer, under the HUD and menus.</summary>
    Models,

    /// <summary>The front layer, over everything including the HUD (effects 1000 to 1002 and 1007).</summary>
    Front,
}
