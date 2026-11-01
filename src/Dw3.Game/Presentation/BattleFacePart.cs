using Godot;

namespace Dw3Production.Presentation;

/// <summary>
/// One animated face rectangle of a battle model texture page: the destination rectangle and the
/// three source positions its frames are copied from.
/// </summary>
/// <param name="Index">Part index: 0 and 1 are the eyes (blink frames), the rest cycle every 6 frames.</param>
/// <param name="X">Destination left edge in page texels (0..255).</param>
/// <param name="Y">Destination top edge in page texels (0..255).</param>
/// <param name="W">Rectangle width in texels.</param>
/// <param name="H">Rectangle height in texels.</param>
/// <param name="Frames">Exactly three source top-left corners, one per animation frame.</param>
internal readonly record struct BattleFacePart(int Index, int X, int Y, int W, int H, Vector2I[] Frames);
