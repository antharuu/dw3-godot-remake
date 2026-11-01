namespace Dw3.Formats.Battle;

/// <summary>One polygon of a mesh together with the draw state it was emitted under.</summary>
/// <param name="Indices">Three or four vertex indices.</param>
/// <param name="NormalIndices">Indices into the normal stream when the polygon is lit, otherwise null.</param>
/// <param name="Uvs">Corner UVs. An untextured polygon carries the UVs of the last textured polygon.</param>
/// <param name="Texture">Texture state current when the polygon was emitted.</param>
/// <param name="Lit">Command <c>0xC_</c> flag.</param>
/// <param name="FlatColor">Colour slot 0 at emission time.</param>
/// <param name="Quad">Command <c>0x8_</c> flag: four indices instead of three.</param>
/// <param name="Abr">Command <c>0xE_</c> value: semi-transparency blend mode.</param>
/// <param name="Gouraud">Command <c>0xD_</c> flag.</param>
/// <param name="Textured">Command <c>0x9_</c> flag: the polygon carries its own UV payload.</param>
/// <param name="Flag0xA">Command <c>0xA_</c> flag, kept verbatim.</param>
/// <param name="Flag0xB">Command <c>0xB_</c> flag, kept verbatim.</param>
public sealed record MeshPolygon(
    IReadOnlyList<int> Indices,
    IReadOnlyList<int>? NormalIndices,
    IReadOnlyList<MeshUv> Uvs,
    MeshTextureState Texture,
    bool Lit,
    MeshColor FlatColor,
    bool Quad,
    int Abr,
    bool Gouraud,
    bool Textured,
    bool Flag0xA,
    bool Flag0xB);
