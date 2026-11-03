namespace Dw3.Formats.Mips;

/// <summary>
/// A register value known to the analysis: either a constant, or the word loaded from a watched
/// memory address plus a constant addend (for example a global read once and offset by an immediate).
/// </summary>
public readonly record struct MipsValue
{
    private readonly uint _constantOrAddress;

    /// <summary>Initializes a new instance of the <see cref="MipsValue"/> struct with its discriminant, backing word, and addend; use <see cref="Constant"/> or <see cref="LoadedWord"/> to construct.</summary>
    /// <param name="isLoadedWord">True for a symbolic loaded word; false for a plain constant.</param>
    /// <param name="constantOrAddress">The constant value when <paramref name="isLoadedWord"/> is false; the watched load address when true.</param>
    /// <param name="addend">Addend accumulated by subsequent <c>addiu</c> instructions; always 0 for plain constants.</param>
    private MipsValue(bool isLoadedWord, uint constantOrAddress, int addend)
    {
        IsLoadedWord = isLoadedWord;
        _constantOrAddress = constantOrAddress;
        Addend = addend;
    }

    /// <summary>Gets a value indicating whether true for a symbolic loaded word, false for a plain constant.</summary>
    public bool IsLoadedWord { get; }

    /// <summary>Gets constant added to the loaded word. Always 0 for a plain constant.</summary>
    public int Addend { get; }

    /// <summary>Gets the constant value. Throws for a loaded word.</summary>
    public uint ConstantValue => IsLoadedWord
        ? throw new InvalidOperationException("A loaded-word value has no constant value.")
        : _constantOrAddress;

    /// <summary>Gets the watched address the word was loaded from. Throws for a constant.</summary>
    public uint LoadAddress => IsLoadedWord
        ? _constantOrAddress
        : throw new InvalidOperationException("A constant value has no load address.");

    /// <summary>Creates a plain compile-time or materialized constant value.</summary>
    /// <param name="value">The 32-bit constant to store.</param>
    /// <returns>A new <see cref="MipsValue"/> with <see cref="IsLoadedWord"/> false and no addend.</returns>
    public static MipsValue Constant(uint value) => new(false, value, 0);

    /// <summary>Creates a symbolic value representing the word loaded from a watched RAM address plus an addend.</summary>
    /// <param name="address">Watched RAM address from which the word was loaded.</param>
    /// <param name="addend">Constant offset accumulated since the load (may be negative).</param>
    /// <returns>A new <see cref="MipsValue"/> with <see cref="IsLoadedWord"/> true.</returns>
    public static MipsValue LoadedWord(uint address, int addend) => new(true, address, addend);

    /// <summary>The value plus a sign-extended immediate, as computed by <c>addiu</c>.</summary>
    /// <param name="immediate">Sign-extended 16-bit addend from the instruction.</param>
    /// <returns>A new value with the immediate added; preserves the loaded-word discriminant and shifts the addend for symbolic values.</returns>
    public MipsValue Add(int immediate) => IsLoadedWord
        ? LoadedWord(_constantOrAddress, unchecked(Addend + immediate))
        : Constant(unchecked(_constantOrAddress + (uint)immediate));

    /// <summary>Returns a diagnostic string: <c>0xADDR±0xADDEND</c> for loaded words and <c>0xVALUE</c> for constants.</summary>
    /// <returns>A hex-formatted string suitable for logging and diagnostics.</returns>
    public override string ToString()
    {
        if (!IsLoadedWord)
        {
            return $"0x{_constantOrAddress:X8}";
        }

        string sign = Addend < 0 ? "-" : "+";
        return $"[0x{_constantOrAddress:X8}]{sign}0x{Math.Abs((long)Addend):X}";
    }
}
