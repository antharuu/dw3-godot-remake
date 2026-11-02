using System.Buffers.Binary;
using System.Text;

namespace Dw3.Formats.Binary;

/// <summary>
/// A PlayStation PS-X EXE executable as stored on disc: a 0x800-byte header followed by the text
/// image that the BIOS copies to the load address declared in the header.
/// </summary>
/// <remarks>
/// Header fields used: magic <c>"PS-X EXE"</c> at +0x00, load address (t_addr) at +0x18 and image
/// size (t_size) at +0x1C. A RAM address <c>A</c> inside the image is file offset
/// <c>0x800 + (A - load address)</c>.
/// </remarks>
public sealed class PsxExecutable
{
    /// <summary>Size of the PS-X EXE header that precedes the loaded image.</summary>
    public const int HeaderBytes = 0x800;

    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("PS-X EXE");
    private readonly byte[] _data;

    /// <summary>Initializes a new instance of the <see cref="PsxExecutable"/> class with its parsed header fields; use <see cref="Parse"/> to construct.</summary>
    /// <param name="data">Raw PS-X EXE file bytes, including the 0x800-byte header.</param>
    /// <param name="loadAddress">R3000 RAM address of the first image byte, read from header offset 0x18.</param>
    /// <param name="imageBytes">Number of image bytes declared in the header at offset 0x1C.</param>
    private PsxExecutable(byte[] data, uint loadAddress, int imageBytes)
    {
        _data = data;
        LoadAddress = loadAddress;
        ImageBytes = imageBytes;
    }

    /// <summary>Gets rAM address of the first image byte.</summary>
    public uint LoadAddress { get; }

    /// <summary>Gets number of image bytes the header declares (and the file contains).</summary>
    public int ImageBytes { get; }

    /// <summary>Gets the whole executable file, header included.</summary>
    public ReadOnlySpan<byte> Bytes => _data;

    /// <summary>Validates the header and wraps the executable bytes without copying them.</summary>
    /// <param name="data">Raw PS-X EXE file bytes, including the 0x800-byte header.</param>
    /// <returns>A validated <see cref="PsxExecutable"/> wrapping <paramref name="data"/>.</returns>
    /// <exception cref="FormatException">The magic is wrong or the declared image does not fit the file.</exception>
    public static PsxExecutable Parse(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length < HeaderBytes || !data.AsSpan(0, Magic.Length).SequenceEqual(Magic))
        {
            throw new FormatException("Executable does not start with a PS-X EXE header.");
        }

        uint loadAddress = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(0x18, 4));
        uint imageBytes = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(0x1C, 4));
        if (imageBytes == 0 || imageBytes > (uint)(data.Length - HeaderBytes))
        {
            throw new FormatException($"PS-X EXE declares a {imageBytes}-byte image but the file holds {data.Length - HeaderBytes} bytes after its header.");
        }

        if ((ulong)loadAddress + imageBytes > uint.MaxValue + 1UL)
        {
            throw new FormatException($"PS-X EXE image at 0x{loadAddress:X8} wraps the address space.");
        }

        return new PsxExecutable(data, loadAddress, (int)imageBytes);
    }

    /// <summary>File offset of <paramref name="size"/> bytes at RAM <paramref name="address"/>.</summary>
    /// <param name="address">R3000 RAM address inside the loaded image; must be within [LoadAddress, LoadAddress+ImageBytes).</param>
    /// <param name="size">Number of bytes to access; must be non-negative.</param>
    /// <returns>File byte offset of the requested range (header included, so always ≥ <see cref="HeaderBytes"/>).</returns>
    /// <exception cref="FormatException">The range is not entirely inside the loaded image.</exception>
    public int OffsetOf(uint address, int size)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(size);
        ulong relative = unchecked((ulong)address - LoadAddress);
        if (address < LoadAddress || relative + (ulong)size > (ulong)ImageBytes)
        {
            throw new FormatException($"Address range 0x{address:X8}+{size} is outside the executable image 0x{LoadAddress:X8}+{ImageBytes}.");
        }

        return HeaderBytes + (int)relative;
    }

    /// <summary>The bytes at RAM <paramref name="address"/>.</summary>
    /// <param name="address">R3000 RAM address inside the loaded image.</param>
    /// <param name="size">Number of bytes to read; must be non-negative.</param>
    /// <returns>A span of <paramref name="size"/> bytes from the underlying file buffer starting at <paramref name="address"/>.</returns>
    public ReadOnlySpan<byte> Slice(uint address, int size) => _data.AsSpan(OffsetOf(address, size), size);
}
