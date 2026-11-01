namespace Dw3.Formats.Water;

/// <summary>
/// The object VRAM rebuilt from a stage texture archive: one 64-word by 256-row page per
/// archive entry, laid side by side from VRAM (0x140, 0x100), plus alternate palette rows
/// carried by entry zero for 8-bit parts.
/// </summary>
/// <param name="Pages">Row-major 16-bit VRAM words, <see cref="ObjectTextureArchiveDecoder.PageWords"/> by <see cref="ObjectTextureArchiveDecoder.PageRows"/> each.</param>
/// <param name="AlternatePalettes">Rows of 256 colour words, empty when entry zero has no palette block.</param>
public sealed record ObjectVram(IReadOnlyList<ushort[]> Pages, IReadOnlyList<ushort[]> AlternatePalettes);
