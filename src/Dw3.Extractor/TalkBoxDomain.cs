using Dw3.Formats.Dialogue;

namespace Dw3.Extractor;

/// <summary>The decoded field talk box: its frame sprites, panel texture and four layouts.</summary>
public sealed record TalkBoxDomain(
    IReadOnlyList<TalkBoxFrameImage> Frames,
    TalkBoxPanelImage Panel,
    IReadOnlyList<TalkBoxLayoutRecord> Layouts);
