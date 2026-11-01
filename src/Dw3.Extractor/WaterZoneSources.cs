namespace Dw3.Extractor;

/// <summary>Disc-file digests recorded alongside a decoded water stage.</summary>
/// <param name="OverlaySha256">SHA-256 hex of the stage overlay file that drives the object list.</param>
/// <param name="SheetSha256">SHA-256 hex of the sprite-sheet archive used for the object frames.</param>
/// <param name="TextureSha256">SHA-256 hex of the shared VRAM texture file.</param>
public sealed record WaterZoneSources(string OverlaySha256, string SheetSha256, string TextureSha256);
