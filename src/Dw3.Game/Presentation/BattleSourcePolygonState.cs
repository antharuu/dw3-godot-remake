namespace Dw3Production.Presentation;

/// <summary>
/// Where one source primitive landed inside a <see cref="BattleLitSurface"/>, so the primitive can
/// be re-emitted in its original command-stream order for the ordering-table renderer.
/// </summary>
/// <param name="Surface">Surface holding the emitted vertices.</param>
/// <param name="Bone">Source bone index.</param>
/// <param name="Command">Zero-based position of the primitive in its mesh command stream.</param>
/// <param name="VertexStart">Index of the first emitted vertex inside the surface arrays.</param>
/// <param name="Size">Source corner count: 3 for a triangle, 4 for a quad.</param>
/// <param name="TextureKey">Texture page key from the model JSON.</param>
/// <param name="Abr">Raw draw-state blend value: 0 opaque, 1..4 select PSX ABR modes 0..3.</param>
internal sealed record BattleSourcePolygonState(
    BattleLitSurface Surface,
    int Bone,
    int Command,
    int VertexStart,
    int Size,
    int TextureKey,
    int Abr);
