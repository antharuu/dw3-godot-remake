using Godot;

namespace Dw3Production.Presentation;

/// <summary>
/// One decoded 256x256 texture page of a battle model, split into the immutable decoded pixels
/// and a working copy that face animation patches in place.
/// </summary>
/// <param name="Source">Decoded page as written by the extractor; never modified.</param>
/// <param name="Working">Page currently shown, with the face rectangles copied in.</param>
/// <param name="Texture">GPU texture uploaded from <paramref name="Working"/>.</param>
internal sealed record BattleFaceTexture(Image Source, Image Working, ImageTexture Texture);
