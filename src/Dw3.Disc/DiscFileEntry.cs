namespace Dw3.Disc;

/// <summary>Describes a file entry found in the ISO9660 directory tree.</summary>
/// <param name="IsoPath">
/// Relative path using forward slashes; root files have no leading slash
/// (e.g. <c>SLES_039.36</c>, <c>STAGE/WSTAG200.BIN</c>).
/// </param>
/// <param name="Lba">Logical Block Address of the first data sector.</param>
/// <param name="Size">Data length in bytes.</param>
public record DiscFileEntry(string IsoPath, int Lba, int Size);
