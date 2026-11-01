using Dw3.Formats.Mips;

namespace Dw3.Formats.Encounter;

/// <summary>
/// Collects potential FieldBattles array addresses from an overlay's MIPS instructions.
/// </summary>
internal static class StageBattleCandidates
{
    private const int RegisterA0 = 4;

    /// <summary>
    /// Collects addresses stored directly to the stage battle field.
    /// </summary>
    /// <param name="overlay">Raw overlay bytes.</param>
    /// <param name="fieldStateAddress">Address of the field state.</param>
    /// <param name="battlesFieldOffset">Offset of the battle field.</param>
    /// <param name="overlayBase">Overlay load address.</param>
    /// <returns>Confirmed battle array addresses.</returns>
    internal static HashSet<uint> CollectDirect(
        ReadOnlySpan<byte> overlay,
        uint fieldStateAddress,
        short battlesFieldOffset,
        uint overlayBase)
    {
        var addresses = new HashSet<uint>();
        foreach (MipsStoreMatch store in MipsStoreScanner.FindStores(overlay, fieldStateAddress, battlesFieldOffset))
        {
            if (!store.Value.IsLoadedWord && store.Value.ConstantValue >= overlayBase)
            {
                addresses.Add(store.Value.ConstantValue);
            }
        }

        return addresses;
    }

    /// <summary>
    /// Collects values in the first call argument at MIPS call sites.
    /// </summary>
    /// <param name="overlay">Raw overlay bytes.</param>
    /// <param name="overlayBase">Overlay load address.</param>
    /// <returns>Distinct address candidates.</returns>
    internal static List<uint> CollectCallArguments(ReadOnlySpan<byte> overlay, uint overlayBase)
    {
        MipsInstruction[] instructions = [.. MipsInstruction.DecodeRange(overlay, 0, MipsInstruction.WholeWordEnd(overlay.Length))];
        var tracker = new MipsRegisterTracker();
        var addresses = new List<uint>();
        var seen = new HashSet<uint>();
        uint overlayEnd = overlayBase + (uint)overlay.Length;

        for (int index = 0; index < instructions.Length; index++)
        {
            MipsInstruction instruction = instructions[index];
            CollectCallArgument(instructions, index, tracker, overlayBase, overlayEnd, seen, addresses);
            tracker.Execute(instruction);
        }

        return addresses;
    }

    /// <summary>
    /// Collects all address values materialized by immediate instructions.
    /// </summary>
    /// <param name="overlay">Raw overlay bytes.</param>
    /// <param name="overlayBase">Overlay load address.</param>
    /// <returns>Distinct address candidates.</returns>
    internal static List<uint> CollectMaterialized(ReadOnlySpan<byte> overlay, uint overlayBase)
    {
        var tracker = new MipsRegisterTracker();
        var addresses = new List<uint>();
        var seen = new HashSet<uint>();
        uint overlayEnd = overlayBase + (uint)overlay.Length;

        foreach (MipsInstruction instruction in MipsInstruction.DecodeRange(overlay, 0, MipsInstruction.WholeWordEnd(overlay.Length)))
        {
            tracker.Execute(instruction);
            if (instruction.Operation is MipsOperation.Addiu or MipsOperation.Ori)
            {
                AddOverlayAddress(tracker[instruction.Rt], overlayBase, overlayEnd, seen, addresses);
            }
        }

        return addresses;
    }

    private static void CollectCallArgument(
        IReadOnlyList<MipsInstruction> instructions,
        int index,
        MipsRegisterTracker tracker,
        uint overlayBase,
        uint overlayEnd,
        HashSet<uint> seen,
        List<uint> addresses)
    {
        MipsInstruction instruction = instructions[index];
        if (instruction.Operation is not (MipsOperation.Jal or MipsOperation.Jalr))
        {
            return;
        }

        MipsValue? value = tracker[RegisterA0];
        if (index + 1 < instructions.Count)
        {
            value = ApplyDelaySlot(value, instructions[index + 1]);
        }

        AddOverlayAddress(value, overlayBase, overlayEnd, seen, addresses);
    }

    private static void AddOverlayAddress(
        MipsValue? value,
        uint overlayBase,
        uint overlayEnd,
        HashSet<uint> seen,
        List<uint> addresses)
    {
        if (value is { IsLoadedWord: false } constant
            && constant.ConstantValue >= overlayBase
            && constant.ConstantValue < overlayEnd
            && seen.Add(constant.ConstantValue))
        {
            addresses.Add(constant.ConstantValue);
        }
    }

    private static MipsValue? ApplyDelaySlot(MipsValue? current, MipsInstruction instruction)
    {
        if (instruction.Operation == MipsOperation.Addiu && instruction.Rt == RegisterA0 && instruction.Rs == RegisterA0)
        {
            return current?.Add(instruction.SignedImmediate);
        }

        if (instruction.Operation == MipsOperation.Ori
            && instruction.Rt == RegisterA0
            && instruction.Rs == RegisterA0
            && current is { IsLoadedWord: false } value)
        {
            return MipsValue.Constant(value.ConstantValue | instruction.UnsignedImmediate);
        }

        return current;
    }
}
