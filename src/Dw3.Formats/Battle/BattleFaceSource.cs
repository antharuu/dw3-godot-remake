namespace Dw3.Formats.Battle;

/// <summary>
/// Layout offsets that locate the face animation data inside the fighter's metadata binary block,
/// recorded verbatim so the extractor can reproduce the exact source addresses for documentation.
/// </summary>
/// <param name="RecordOffset">Byte offset of the FaceInfo record within the metadata block.</param>
/// <param name="FacePointerOffset">Byte offset of the face pointer within the FaceInfo record.</param>
/// <param name="FaceOffset">Absolute byte offset of the face data within the metadata block.</param>
/// <param name="RecordSize">Byte size of one FaceInfo record as read from the header.</param>
/// <param name="Terminator">Terminator string found at the end of the face section (diagnostic only).</param>
public sealed record BattleFaceSource(int RecordOffset, int FacePointerOffset, int FaceOffset, int RecordSize, string Terminator);
