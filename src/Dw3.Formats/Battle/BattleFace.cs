namespace Dw3.Formats.Battle;

/// <summary>
/// The decoded face animation for one battle fighter: the source layout that locates
/// the data in the metadata binary, and the sprite parts that compose the face.
/// </summary>
/// <param name="Source">Address layout used to find this face in the metadata block.</param>
/// <param name="Parts">Ordered list of sprite parts that make up the animated face.</param>
public sealed record BattleFace(BattleFaceSource Source, IReadOnlyList<BattleFacePart> Parts);
