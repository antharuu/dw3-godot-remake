namespace Dw3.Formats.Mips;

/// <summary>
/// A register value known to the analysis: either a constant, or the word loaded from a watched
/// memory address plus a constant addend (for example a global read once and offset by an immediate).
/// </summary>
public readonly record struct MipsValue
{
    private MipsValue(bool isLoadedWord, uint constantOrAddress, int addend)
    {
        IsLoadedWord = isLoadedWord;
        _constantOrAddress = constantOrAddress;
        Addend = addend;
    }

    private readonly uint _constantOrAddress;

    /// <summary>True for a symbolic loaded word, false for a plain constant.</summary>
    public bool IsLoadedWord { get; }

    /// <summary>Constant added to the loaded word. Always 0 for a plain constant.</summary>
    public int Addend { get; }

    /// <summary>The constant value. Throws for a loaded word.</summary>
    public uint ConstantValue => IsLoadedWord
        ? throw new InvalidOperationException("A loaded-word value has no constant value.")
        : _constantOrAddress;

    /// <summary>The watched address the word was loaded from. Throws for a constant.</summary>
    public uint LoadAddress => IsLoadedWord
        ? _constantOrAddress
        : throw new InvalidOperationException("A constant value has no load address.");

    public static MipsValue Constant(uint value) => new(false, value, 0);
    public static MipsValue LoadedWord(uint address, int addend) => new(true, address, addend);

    /// <summary>The value plus a sign-extended immediate, as computed by <c>addiu</c>.</summary>
    public MipsValue Add(int immediate) => IsLoadedWord
        ? LoadedWord(_constantOrAddress, unchecked(Addend + immediate))
        : Constant(unchecked(_constantOrAddress + (uint)immediate));

    public override string ToString() => IsLoadedWord
        ? $"[0x{_constantOrAddress:X8}]{(Addend < 0 ? "-" : "+")}0x{Math.Abs((long)Addend):X}"
        : $"0x{_constantOrAddress:X8}";
}
