namespace Dw3.Formats.Battle;

/// <summary>Byte range [<see cref="Start"/>, <see cref="End"/>) of one archive entry; (0, 0) marks an absent entry.</summary>
public readonly record struct ArchiveRange(int Start, int End)
{
    /// <summary>True for an absent (zero-offset) entry.</summary>
    public bool IsEmpty => Start == End;
}
