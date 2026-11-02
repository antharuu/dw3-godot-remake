using Dw3Production.Conversion;
using Dw3Production.Presentation;
using Dw3Production.Simulation;
using Godot;

namespace Dw3Production.Diagnostics.Field;

/// <summary>
/// What the dialogue probe needs on top of the field session: the conversation state machine,
/// the dialogue tables and the talk-box presenter with the textures it draws.
/// </summary>
internal interface IDialogueProbeHost : IFieldProbeHost
{
    /// <summary>Gets the conversation state machine that the field simulation ticks.</summary>
    DialogueController Dialogue { get; }

    /// <summary>Gets the conversations and font, or <see langword="null"/> before startup loaded them.</summary>
    DialogueData? DialogueData { get; }

    /// <summary>Gets the talk-box presenter.</summary>
    DialogueRenderer DialogueRenderer { get; }

    /// <summary>Gets the talk-box frame sprites and per-type layouts, or <see langword="null"/> before startup.</summary>
    DialogueFrameData? DialogueFrames { get; }

    /// <summary>Gets the uploaded talk-box frame sprites keyed by frame id, or <see langword="null"/> before startup.</summary>
    IReadOnlyDictionary<int, Texture2D>? FrameTextures { get; }

    /// <summary>Gets the uploaded talk-box panel texture.</summary>
    Texture2D PanelTexture { get; }

    /// <summary>Gets the width of the open talk box in source pixels, fixed when the conversation started.</summary>
    int DialogueBoxWidth { get; }

    /// <summary>Builds the view the talk-box presenter draws for the open conversation.</summary>
    /// <returns>The talk-box view.</returns>
    DialogueView BuildDialogueView();
}
