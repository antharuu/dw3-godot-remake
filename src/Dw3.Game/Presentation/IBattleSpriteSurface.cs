namespace Dw3Production.Presentation;

/// <summary>The drawing side of the sprite-effect consumer: real canvas children in production, a recorder in tests.</summary>
internal interface IBattleSpriteSurface
{
    /// <summary>Creates the drawing child of a newly started effect.</summary>
    /// <param name="instance">Started effect.</param>
    void Attach(BattleSpriteEffectInstance instance);

    /// <summary>Hides and destroys the drawing child of a finished or cancelled effect.</summary>
    /// <param name="instance">Removed effect.</param>
    void Detach(BattleSpriteEffectInstance instance);

    /// <summary>Draws the current samples of every active effect and the summed white-flash level.</summary>
    /// <param name="active">Active effects in creation order.</param>
    /// <param name="flashLevel">Summed additive white level, clamped to 255.</param>
    void Present(IReadOnlyList<BattleSpriteEffectInstance> active, int flashLevel);
}
