using Dw3Production.Conversion;
using Dw3Production.Diagnostics.Field;
using Dw3Production.Presentation;
using Dw3Production.Simulation;
using Godot;

namespace Dw3Production;

/// <summary>
/// Exposes the running conversation and talk-box presentation to the dialogue probe through
/// <see cref="IDialogueProbeHost"/>. Every member forwards to the live host state.
/// </summary>
public partial class GameHost : IDialogueProbeHost
{
    /// <inheritdoc/>
    DialogueController IDialogueProbeHost.Dialogue => _dialogue;

    /// <inheritdoc/>
    DialogueData? IDialogueProbeHost.DialogueData => _dialogueData;

    /// <inheritdoc/>
    DialogueRenderer IDialogueProbeHost.DialogueRenderer => DialogueRenderer;

    /// <inheritdoc/>
    DialogueFrameData? IDialogueProbeHost.DialogueFrames => _dialogueFrames;

    /// <inheritdoc/>
    IReadOnlyDictionary<int, Texture2D>? IDialogueProbeHost.FrameTextures => _frameTextures;

    /// <inheritdoc/>
    Texture2D IDialogueProbeHost.PanelTexture => PanelTexture;

    /// <inheritdoc/>
    int IDialogueProbeHost.DialogueBoxWidth => _dialogueBoxWidth;

    /// <inheritdoc/>
    DialogueView IDialogueProbeHost.BuildDialogueView() => BuildDialogueView();
}
