namespace Dw3.Formats.Field;

/// <summary>Four entries of the FIELDCOM archive stored at DAT/FIELD/FIELDCOM.BIN.</summary>
public sealed record FieldcomArchive(IReadOnlyList<ReadOnlyMemory<byte>> Entries);
