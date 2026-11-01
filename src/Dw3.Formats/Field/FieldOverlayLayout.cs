namespace Dw3.Formats.Field;

/// <summary>Memory layout facts shared by readers of the WSTAG stage overlays.</summary>
public static class FieldOverlayLayout
{
    /// <summary>
    /// RAM address at which the supported executable loads every WSTAG overlay file;
    /// overlay file offset 0 maps to this address.
    /// </summary>
    public const uint LoadAddress = 0x800A5DE0;
}
