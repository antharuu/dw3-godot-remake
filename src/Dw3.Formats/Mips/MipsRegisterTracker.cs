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
    private readonly HashSet<uint> _watchedLoads;
    private MipsOperation _pendingAfterDelaySlot = MipsOperation.Other;

    /// <summary>Initializes a new instance of the <see cref="MipsRegisterTracker"/> class with <c>$zero</c> pre-set to 0 and the optional set of watched load addresses.</summary>
    /// <param name="watchedLoads">Word addresses whose loads stay symbolic instead of unknown.</param>
    public MipsRegisterTracker(IEnumerable<uint>? watchedLoads = null)
    {
        _watchedLoads = watchedLoads is null ? [] : [.. watchedLoads];
        _registers[0] = MipsValue.Constant(0);
    }

    /// <summary>The known value of a register, or null.</summary>
    /// <param name="register">General-purpose register index (0–31); register 0 always returns 0.</param>
    public MipsValue? this[int register] => _registers[register];

    /// <summary>
    /// Applies one instruction. Returns the store it performs when its base address and stored value
    /// are both known, otherwise null.
    /// </summary>
    /// <param name="instruction">Decoded R3000 word to apply to the register state.</param>
    /// <returns>The resolved store if the instruction is <c>sw</c> with both operands known; otherwise null.</returns>
    public MipsStore? Execute(in MipsInstruction instruction)
    {
        MipsOperation pending = _pendingAfterDelaySlot;
        _pendingAfterDelaySlot = instruction.HasDelaySlot ? instruction.Operation : MipsOperation.Other;
        MipsStore? store = ExecuteOperation(instruction);
        ApplyPendingControlTransfer(pending);
        return store;
    }

    /// <summary>Executes the operation-specific register effect or creates its observable store.</summary>
    /// <param name="instruction">Decoded word whose register effects are applied.</param>
    /// <returns>The known store performed by the word, if any.</returns>
    private MipsStore? ExecuteOperation(in MipsInstruction instruction)
    {
        return instruction.Operation switch
        {
            MipsOperation.Lui => WriteAndReturn(instruction.Rt, MipsValue.Constant((uint)instruction.UnsignedImmediate << 16)),
            MipsOperation.Addiu => WriteAndReturn(instruction.Rt, _registers[instruction.Rs]?.Add(instruction.SignedImmediate)),
            MipsOperation.Ori => WriteAndReturn(instruction.Rt, BuildOrValue(instruction)),
            MipsOperation.Addu => WriteAndReturn(instruction.Rd, Combine(instruction.Rs, instruction.Rt, (a, b) => unchecked(a + b))),
            MipsOperation.Or => WriteAndReturn(instruction.Rd, Combine(instruction.Rs, instruction.Rt, (a, b) => a | b)),
            MipsOperation.Lw => WriteAndReturn(instruction.Rt, ReadWatchedValue(instruction)),
            MipsOperation.Sw => CreateStore(instruction),
            _ => ClearDestination(instruction),
        };
    }

    /// <summary>Writes a register for a non-store instruction and returns no store.</summary>
    /// <param name="register">Destination general-purpose register.</param>
    /// <param name="value">Tracked value to retain for the register.</param>
    /// <returns>Always null because this path cannot perform a store.</returns>
    private MipsStore? WriteAndReturn(int register, MipsValue? value)
    {
        Write(register, value);
        return null;
    }

    /// <summary>Builds the result of an <c>ori</c> only when its source register is constant.</summary>
    /// <param name="instruction">Decoded immediate OR operation.</param>
    /// <returns>The materialized constant, or null when the source is unknown.</returns>
    private MipsValue? BuildOrValue(in MipsInstruction instruction) =>
        ConstantOf(instruction.Rs) is uint orBase
            ? MipsValue.Constant(orBase | instruction.UnsignedImmediate)
            : null;

    /// <summary>Keeps a load symbolic only when its effective address is being watched.</summary>
    /// <param name="instruction">Decoded word-load operation.</param>
    /// <returns>A symbolic loaded word, or null for an untracked address.</returns>
    private MipsValue? ReadWatchedValue(in MipsInstruction instruction) =>
        AddressOf(instruction) is uint loadAddress && _watchedLoads.Contains(loadAddress)
            ? MipsValue.LoadedWord(loadAddress, 0)
            : null;

    /// <summary>Creates the observed store when both its target address and source value are known.</summary>
    /// <param name="instruction">Decoded store operation.</param>
    /// <returns>The resolved store, or null when either operand is unknown.</returns>
    private MipsStore? CreateStore(in MipsInstruction instruction) =>
        AddressOf(instruction) is uint storeAddress && _registers[instruction.Rt] is MipsValue stored
            ? new MipsStore(instruction.Offset, storeAddress, stored)
            : null;

    /// <summary>Forgets the destination of an operation outside the tracked subset.</summary>
    /// <param name="instruction">Decoded operation whose destination may be overwritten.</param>
    /// <returns>Always null because unknown operations are not reported as stores.</returns>
    private MipsStore? ClearDestination(in MipsInstruction instruction)
    {
        if (instruction.Destination is int destination)
        {
            Write(destination, null);
        }

        return null;
    }

    /// <summary>Applies the clobber that follows the preceding control instruction's delay slot.</summary>
    /// <param name="pending">Control operation whose delay slot just completed.</param>
    private void ApplyPendingControlTransfer(MipsOperation pending)
    {
        if (pending is MipsOperation.Jal or MipsOperation.Jalr)
        {
            ClearCallerSavedRegisters();
        }
        else if (pending is MipsOperation.Jr)
        {
            ClearAllRegisters();
        }
    }

    /// <summary>Forgets ABI caller-saved registers after a completed call delay slot.</summary>
    private void ClearCallerSavedRegisters()
    {
        foreach (int register in CallerSaved)
        {
            Write(register, null);
        }
    }

    /// <summary>Forgets every mutable register after a return delay slot.</summary>
    private void ClearAllRegisters()
    {
        for (int register = 1; register < _registers.Length; register++)
        {
            Write(register, null);
        }
    }

    /// <summary>Sets <paramref name="register"/> to <paramref name="value"/>; ignores writes to <c>$zero</c> (register 0).</summary>
    /// <param name="register">General-purpose register index (0–31).</param>
    /// <param name="value">New tracked value, or null to mark the register unknown.</param>
    private void Write(int register, MipsValue? value)
    {
        if (register != 0)
        {
            _registers[register] = value;
        }
    }

    /// <summary>Returns the constant backing value of <paramref name="register"/> when it holds a plain constant; otherwise null.</summary>
    /// <param name="register">General-purpose register index (0–31).</param>
    /// <returns>The 32-bit constant value, or null if the register holds a symbolic loaded word or is unknown.</returns>
    private uint? ConstantOf(int register) =>
        _registers[register] is MipsValue { IsLoadedWord: false } value ? value.ConstantValue : null;

    /// <summary>Computes the effective address of a load or store: base register constant plus the sign-extended displacement.</summary>
    /// <param name="instruction">Decoded load or store instruction whose Rs and SignedImmediate fields are used.</param>
    /// <returns>The resolved target address, or null when the base register is unknown.</returns>
    private uint? AddressOf(in MipsInstruction instruction) =>
        ConstantOf(instruction.Rs) is uint baseAddress ? unchecked(baseAddress + (uint)instruction.SignedImmediate) : null;

    // A copy through $zero keeps a symbolic value. Two constants combine. Anything else is unknown.

    /// <summary>Combines two register values via <paramref name="operation"/>; propagates a copy through <c>$zero</c> and produces null when either operand is unknown.</summary>
    /// <param name="left">Source register index for the left operand.</param>
    /// <param name="right">Source register index for the right operand.</param>
    /// <param name="operation">Arithmetic or bitwise function applied when both operands are known constants.</param>
    /// <returns>The combined constant value, a propagated symbolic value when one operand is <c>$zero</c>, or null when the result cannot be determined.</returns>
    private MipsValue? Combine(int left, int right, Func<uint, uint, uint> operation)
    {
        MipsValue? a = _registers[left];
        MipsValue? b = _registers[right];
        if (right == 0)
        {
            return a;
        }

        if (left == 0)
        {
            return b;
        }

        return a is { IsLoadedWord: false } ca && b is { IsLoadedWord: false } cb
            ? MipsValue.Constant(operation(ca.ConstantValue, cb.ConstantValue))
            : null;
    }
}
