namespace Dw3.Formats.Field;

/// <summary>One NPC disc lookup entry linking a character slot to a file number and archive entry.</summary>
public sealed record NpcFileEntry(int FileNumber, int ArchiveEntry);
