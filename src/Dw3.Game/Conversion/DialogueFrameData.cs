namespace Dw3Production.Conversion;

/// <summary>The talk-box contract: the seven frame PNGs, the four layouts and the panel.</summary>
public sealed record DialogueFrameData(IReadOnlyDictionary<int, DialogueFrame> Frames, IReadOnlyList<TalkBoxLayout> Layouts, PanelSpec Panel);
