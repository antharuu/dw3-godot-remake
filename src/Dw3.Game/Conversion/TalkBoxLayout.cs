namespace Dw3Production.Conversion;

/// <summary>One of the executable's four talk-box layout records: the offsets of every box part,
/// in PSX screen pixels, relative to the box origin.</summary>
public sealed record TalkBoxLayout(int Sprite, Vec2I SpritePos, Vec2I Sprite0Pos, Vec2I Panel, Vec2I Sprite2Pos, int NameX, int NameY, int TextX, int TextY, Vec2I Arrow);
