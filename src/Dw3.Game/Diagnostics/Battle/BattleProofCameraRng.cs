using Dw3Production.Presentation;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>Pins only the source shot-selection roll during repeatable framebuffer evidence.</summary>
internal sealed class BattleProofCameraRng : IShotCameraRng
{
    /// <inheritdoc/>
    public int NextRoll() => 0;
}
