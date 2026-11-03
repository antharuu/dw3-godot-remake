using Dw3Production.Conversion;
using Godot;

namespace Dw3Production.Presentation;

/// <summary>The render view for one frame of the talk box, computed by the host from the actor's
/// source position, the camera and the dialogue controller's reveal state.</summary>
public readonly record struct DialogueView(
    Vector2 BoxOrigin,
    int Type,
    int BoxWidth,
    IReadOnlyList<TextToken> SpeakerName,
    bool SpeakerIsPlayer,
    IReadOnlyList<TextToken> Page,
    int RevealCount,
    bool ShowArrow);
