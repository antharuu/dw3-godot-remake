namespace Dw3.Formats.Dialogue;

/// <summary>Maps the twelve talk-box sprite anchors stored in one executable layout record.</summary>
public sealed record TalkBoxLayoutRecord(
    int Sprite,
    int SpriteX, int SpriteY,
    int Sprite0X, int Sprite0Y,
    int PanelX, int PanelY,
    int Sprite2X, int Sprite2Y,
    int NameX, int NameY,
    int TextX, int TextY,
    int ArrowX, int ArrowY);
