namespace Dw3.Formats.Dialogue;

/// <summary>
/// A language-specific stage-talk member resolved from an engine file selector.
/// </summary>
/// <param name="Selector">File id of language copy zero written by stage setup code.</param>
/// <param name="FileId">Engine file id of the selected language-specific disc member.</param>
/// <param name="IsoPath">ISO path of the matching disc member.</param>
/// <param name="TalkIndex">Two-digit index parsed from its <c>?STALKNN.BIN</c> name.</param>
public sealed record StageTextFile(int Selector, int FileId, string IsoPath, int TalkIndex);
