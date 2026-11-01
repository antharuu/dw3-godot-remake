namespace Dw3.Formats.Mips;

/// <summary>
/// Conservative straight-line register tracking over a decoded instruction stream.
/// </summary>
/// <remarks>
/// This is not an emulator. Only the materialization idioms the compiler emits for addresses and
/// small constants are followed: <c>lui</c>, <c>addiu</c>, <c>ori</c>, <c>addu</c>/<c>or</c> with a
/// known operand, and <c>lw</c> from a caller-watched address (kept symbolic). Any other writer makes
/// its destination unknown. Branches are not followed: a value is only trusted along the linear code
/// path, so a store seen with a known value is one the analysis has actually materialized. A call
/// (<c>jal</c>/<c>jalr</c>) clobbers the caller-saved registers once its delay slot has executed, and a
/// <c>jr</c> ends the function, forgetting every register once its delay slot has executed.
/// </remarks>
public sealed class MipsRegisterTracker
{
    // $at, $v0-$v1, $a0-$a3, $t0-$t7, $t8-$t9 and $ra: not preserved across a call.
    private static readonly int[] CallerSaved = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 24, 25, 31];

    private readonly MipsValue?[] _registers = new MipsValue?[32];
    private readonly IReadOnlySet<uint> _watchedLoads;
    private MipsOperation _pendingAfterDelaySlot = MipsOperation.Other;

    /// <param name="watchedLoads">Word addresses whose loads stay symbolic instead of unknown.</param>
    public MipsRegisterTracker(IEnumerable<uint>? watchedLoads = null)
    {
        _watchedLoads = (watchedLoads ?? []).ToHashSet();
        _registers[0] = MipsValue.Constant(0);
    }

    /// <summary>The known value of a register, or null.</summary>
    public MipsValue? this[int register] => _registers[register];

    /// <summary>
    /// Applies one instruction. Returns the store it performs when its base address and stored value
    /// are both known, otherwise null.
    /// </summary>
    public MipsStore? Execute(in MipsInstruction instruction)
    {
        MipsOperation pending = _pendingAfterDelaySlot;
        _pendingAfterDelaySlot = instruction.HasDelaySlot ? instruction.Operation : MipsOperation.Other;

        MipsStore? store = null;
        switch (instruction.Operation)
        {
            case MipsOperation.Lui:
                Write(instruction.Rt, MipsValue.Constant((uint)instruction.UnsignedImmediate << 16));
                break;
            case MipsOperation.Addiu:
                Write(instruction.Rt, _registers[instruction.Rs]?.Add(instruction.SignedImmediate));
                break;
            case MipsOperation.Ori:
                Write(instruction.Rt, ConstantOf(instruction.Rs) is uint orBase
                    ? MipsValue.Constant(orBase | instruction.UnsignedImmediate)
                    : null);
                break;
            case MipsOperation.Addu:
                Write(instruction.Rd, Combine(instruction.Rs, instruction.Rt, (a, b) => unchecked(a + b)));
                break;
            case MipsOperation.Or:
                Write(instruction.Rd, Combine(instruction.Rs, instruction.Rt, (a, b) => a | b));
                break;
            case MipsOperation.Lw:
                Write(instruction.Rt, AddressOf(instruction) is uint loadAddress && _watchedLoads.Contains(loadAddress)
                    ? MipsValue.LoadedWord(loadAddress, 0)
                    : null);
                break;
            case MipsOperation.Sw:
                if (AddressOf(instruction) is uint storeAddress && _registers[instruction.Rt] is MipsValue stored)
                    store = new MipsStore(instruction.Offset, storeAddress, stored);
                break;
            default:
                if (instruction.Destination is int destination)
                    Write(destination, null);
                break;
        }

        // A jump's own effects (the $ra link) happen at once, the clobber after the delay slot.
        if (pending is MipsOperation.Jal or MipsOperation.Jalr)
            foreach (int register in CallerSaved)
                Write(register, null);
        else if (pending is MipsOperation.Jr)
            for (int register = 1; register < _registers.Length; register++)
                Write(register, null);
        return store;
    }

    private void Write(int register, MipsValue? value)
    {
        if (register != 0)
            _registers[register] = value;
    }

    private uint? ConstantOf(int register) =>
        _registers[register] is MipsValue { IsLoadedWord: false } value ? value.ConstantValue : null;

    private uint? AddressOf(in MipsInstruction instruction) =>
        ConstantOf(instruction.Rs) is uint baseAddress ? unchecked(baseAddress + (uint)instruction.SignedImmediate) : null;

    // A copy through $zero keeps a symbolic value. Two constants combine. Anything else is unknown.
    private MipsValue? Combine(int left, int right, Func<uint, uint, uint> operation)
    {
        MipsValue? a = _registers[left];
        MipsValue? b = _registers[right];
        if (right == 0)
            return a;
        if (left == 0)
            return b;
        return a is { IsLoadedWord: false } ca && b is { IsLoadedWord: false } cb
            ? MipsValue.Constant(operation(ca.ConstantValue, cb.ConstantValue))
            : null;
    }
}

/// <summary>A word store whose target address and value were both materialized.</summary>
/// <param name="Offset">Byte offset of the <c>sw</c> in the analysed buffer.</param>
/// <param name="Address">Absolute target address (base register plus displacement).</param>
/// <param name="Value">The stored value.</param>
public readonly record struct MipsStore(int Offset, uint Address, MipsValue Value);
