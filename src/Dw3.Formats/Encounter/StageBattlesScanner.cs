using System.Buffers.Binary;
using Dw3.Formats.Mips;

namespace Dw3.Formats.Encounter;

/// <summary>
/// Discovers the FieldBattles arrays in a stage overlay by two complementary strategies:
///
/// Strategy A — Direct store: scans the setup MIPS code for direct stores to
///   FIELDSTG_state.battles (FIELDSTG_state at 0x80099D80, battles field offset 0x20).
///   Most stages assign <c>FIELDSTG_state.battles = stageBattles;</c> directly.
///
/// Strategy B — FindBattles argument: some stages call
///   <c>FIELDSTG_state.battles = FIELDSTG_state.findBattles(stageBattles, GAME.place);</c>.
///   The result of this call (stored to FIELDSTG_state.battles) is not a static constant.
///   Instead, the scanner captures the value of $a0 (first argument) at each jalr/jal
///   instruction, which is the stageBattles array address passed to findBattles.
///
/// Both strategies validate each candidate address against the structural constraints of
/// the FieldBattles record before recording it.
///
/// Each WSTAG*.PRO stage overlay's setup function stores stageBattles into FIELDSTG_state.
/// The array is not self-terminating: entry count is determined by validating each
/// FieldBattles record until structural validation fails (max 30 entries, per
/// FIELDSTG_findBattles which scans up to 30 entries).
/// </summary>
public static class StageBattlesScanner
{
    /// <summary>Maximum FieldBattles entries per stage (FIELDSTG_findBattles scans up to 30).</summary>
    private const int MaxEntries = 30;

    // $a0 is register 4 (first function argument in MIPS O32 calling convention).
    private const int RegisterA0 = 4;

    /// <summary>
    /// Scans <paramref name="overlay"/> for stageBattles stores and returns all discovered
    /// FieldBattles arrays with their decoded entries.
    ///
    /// Two code patterns are observed on the EU disc:
    ///
    /// Pattern A — single multi-entry array: Strategy A finds multiple consecutive confirmed
    ///   addresses (e.g. A and A+28). The smallest is the array base; the others are element-
    ///   pointer stores made in the same sequential code path (not separate arrays). Structural
    ///   validation sizes the base array without capping by the element-pointer addresses.
    ///
    /// Pattern B — multiple 1-entry arrays via conditional branches: Strategy A's linear
    ///   tracker sees only the last taken branch and finds ONE confirmed address. The N-1
    ///   other arrays are in untaken branches and are recovered by Strategy C. Each Strategy-C-
    ///   only candidate is capped by the distance to the next adjacent candidate so that
    ///   structurally adjacent but independent 1-entry arrays are not merged.
    /// </summary>
    /// <param name="overlay">Raw WSTAG*.PRO overlay bytes.</param>
    /// <param name="overlayBase">RAM load address of the overlay (EU: 0x800A5DE0).</param>
    /// <param name="fieldStateAddress">RAM address of FIELDSTG_state (EU: 0x80099D80).</param>
    /// <param name="battlesFieldOffset">Byte offset of the battles pointer within FIELDSTG_state (EU: 0x20).</param>
    public static IReadOnlyList<StageBattleArray> Scan(
        ReadOnlySpan<byte> overlay,
        uint overlayBase,
        uint fieldStateAddress,
        short battlesFieldOffset)
    {
        // Strategy A: direct sw to FIELDSTG_state.battles.
        IReadOnlyList<MipsStoreMatch> stores = MipsStoreScanner.FindStores(
            overlay, fieldStateAddress, battlesFieldOffset);

        var confirmed = new HashSet<uint>();
        foreach (MipsStoreMatch store in stores)
        {
            if (!store.Value.IsLoadedWord && store.Value.ConstantValue >= overlayBase)
                confirmed.Add(store.Value.ConstantValue);
        }

        // Strategy B: capture $a0 at jal/jalr call sites with delay-slot awareness
        // (for stages using findBattles(stageBattles, ...) indirect assignment).
        if (confirmed.Count == 0)
        {
            foreach (uint addr in FindCallArgumentA0Values(overlay, overlayBase))
                confirmed.Add(addr);
        }

        // Strategy C: all lui+addiu/ori pairs that produce overlay-range addresses.
        // Recovers Pattern B arrays in untaken branches not seen by the linear tracker.
        var all = new HashSet<uint>(confirmed);
        foreach (uint addr in FindAllOverlayAddresses(overlay, overlayBase))
            all.Add(addr);

        // Classify confirmed addresses (Pattern A vs Pattern B):
        //   Pattern A: multiple confirmed addresses — the smallest is the array base and
        //     the rest are element-pointer stores (they point INTO the base array, not at
        //     separate arrays). An address is only an element pointer when it is a positive,
        //     stride-aligned offset from the base and within MaxEntries strides: this guards
        //     against Strategy B, which can produce confirmed addresses from different call
        //     sites that happen to be at different (non-stride-aligned) overlay locations and
        //     are therefore entirely separate arrays, not element pointers of a single array.
        //   Pattern B: single confirmed address — no element pointers; other arrays are
        //     C-only candidates recovered from untaken branches.
        uint confirmedBase = confirmed.Count > 0 ? confirmed.Min() : uint.MaxValue;
        var elementPointers = new HashSet<uint>(confirmed.Where(a =>
        {
            if (a == confirmedBase) return false;
            long delta = (long)a - confirmedBase;
            return delta > 0
                && delta % FieldEncounterDecoder.FieldBattlesStride == 0
                && delta / FieldEncounterDecoder.FieldBattlesStride < MaxEntries;
        }));

        uint[] sorted = [.. all.OrderBy(a => a)];
        var subsumed = new HashSet<uint>();
        var result = new List<StageBattleArray>();

        for (int i = 0; i < sorted.Length; i++)
        {
            uint arrayAddress = sorted[i];

            if (subsumed.Contains(arrayAddress))
                continue;

            // Pattern A element pointers are accounted for by the base array's subsumed set.
            if (elementPointers.Contains(arrayAddress))
                continue;

            int maxEntries;
            if (confirmed.Contains(arrayAddress))
            {
                // Confirmed base: no upper bound — structural validation determines count.
                maxEntries = MaxEntries;
            }
            else
            {
                // Strategy-C-only (Pattern B): cap by next adjacent candidate so that
                // consecutive independent 1-entry arrays are not merged.
                uint nextCandidate = i + 1 < sorted.Length ? sorted[i + 1] : uint.MaxValue;
                maxEntries = nextCandidate > arrayAddress
                    ? (int)Math.Min(
                        (nextCandidate - arrayAddress) / (uint)FieldEncounterDecoder.FieldBattlesStride,
                        MaxEntries)
                    : MaxEntries;
            }

            int count = CountValidEntries(overlay, arrayAddress, overlayBase, maxEntries);
            if (count == 0)
                continue;

            // Mark inner elements (indices 1..count-1) so they are not emitted as separate arrays.
            for (int k = 1; k < count; k++)
                subsumed.Add(arrayAddress + (uint)(k * FieldEncounterDecoder.FieldBattlesStride));

            var places = new List<FieldBattles>(count);
            for (int j = 0; j < count; j++)
            {
                uint entryAddr = arrayAddress + (uint)(j * FieldEncounterDecoder.FieldBattlesStride);
                places.Add(FieldEncounterDecoder.DecodeFieldBattles(overlay, entryAddr, overlayBase));
            }
            result.Add(new StageBattleArray(arrayAddress, count, places));
        }
        return result;
    }

    // ── Strategy B: $a0 at call sites ─────────────────────────────────────────

    /// <summary>
    /// Returns all distinct values held in $a0 (register 4) at jal/jalr call sites,
    /// accounting for any $a0 modification in the delay slot (which executes before the call
    /// takes effect in MIPS). Used for stages that call findBattles(stageBattles, ...) where
    /// the stageBattles address is often finalized in the delay slot of the jalr.
    /// </summary>
    private static IReadOnlyList<uint> FindCallArgumentA0Values(
        ReadOnlySpan<byte> overlay, uint overlayBase)
    {
        var tracker = new MipsRegisterTracker();
        var seen = new HashSet<uint>();
        var result = new List<uint>();
        uint overlayEnd = overlayBase + (uint)overlay.Length;

        MipsInstruction[] instructions = [.. MipsInstruction.DecodeRange(overlay, 0, MipsInstruction.WholeWordEnd(overlay.Length))];

        for (int idx = 0; idx < instructions.Length; idx++)
        {
            MipsInstruction instruction = instructions[idx];

            if (instruction.Operation is MipsOperation.Jal or MipsOperation.Jalr)
            {
                // $a0 may still be partially formed (upper half only): if the next instruction
                // (delay slot) is an addiu/ori that modifies $a0, apply it to get the full value.
                MipsValue? a0 = tracker[RegisterA0];

                if (idx + 1 < instructions.Length)
                    a0 = ApplyDelaySlotToA0(a0, instructions[idx + 1]);

                if (a0 is { IsLoadedWord: false } v
                    && v.ConstantValue >= overlayBase
                    && v.ConstantValue < overlayEnd
                    && seen.Add(v.ConstantValue))
                {
                    result.Add(v.ConstantValue);
                }
            }
            tracker.Execute(instruction);
        }
        return result;
    }

    /// <summary>
    /// Applies the effect of a delay-slot instruction to a candidate $a0 value.
    /// Only handles the immediate-operand forms that compilers emit to finalize an address:
    /// addiu $a0, $a0, imm and ori $a0, $a0, imm.
    /// </summary>
    private static MipsValue? ApplyDelaySlotToA0(MipsValue? current, MipsInstruction delaySlot)
    {
        // addiu $a0 ($rt=4), $a0 ($rs=4), imm
        if (delaySlot.Operation == MipsOperation.Addiu
            && delaySlot.Rt == RegisterA0 && delaySlot.Rs == RegisterA0)
        {
            return current?.Add(delaySlot.SignedImmediate);
        }
        // ori $a0, $a0, imm (zero-extended)
        if (delaySlot.Operation == MipsOperation.Ori
            && delaySlot.Rt == RegisterA0 && delaySlot.Rs == RegisterA0
            && current is { IsLoadedWord: false } cv)
        {
            return MipsValue.Constant(cv.ConstantValue | delaySlot.UnsignedImmediate);
        }
        return current;
    }

    // ── Strategy C: all overlay-range address materializations ────────────────

    /// <summary>
    /// Scans all lui+addiu/ori pairs in the overlay code that produce constants in the overlay
    /// RAM address range. Covers stages that select among multiple stageBattlesN arrays via
    /// conditional branches into the same sw instruction (the tracker would only see the last
    /// value; this finds all candidates materialized in the instruction stream).
    /// </summary>
    private static IReadOnlyList<uint> FindAllOverlayAddresses(
        ReadOnlySpan<byte> overlay, uint overlayBase)
    {
        var tracker = new MipsRegisterTracker();
        var seen = new HashSet<uint>();
        var result = new List<uint>();
        uint overlayEnd = overlayBase + (uint)overlay.Length;

        foreach (MipsInstruction instruction in MipsInstruction.DecodeRange(
            overlay, 0, MipsInstruction.WholeWordEnd(overlay.Length)))
        {
            // After each addiu/ori that finalizes a register, check if its value is in overlay range.
            tracker.Execute(instruction);

            if (instruction.Operation is MipsOperation.Addiu or MipsOperation.Ori)
            {
                MipsValue? val = tracker[instruction.Rt];
                if (val is { IsLoadedWord: false } v
                    && v.ConstantValue >= overlayBase
                    && v.ConstantValue < overlayEnd
                    && seen.Add(v.ConstantValue))
                {
                    result.Add(v.ConstantValue);
                }
            }
        }
        return result;
    }

    // ── Counting ──────────────────────────────────────────────────────────────

    private static int CountValidEntries(ReadOnlySpan<byte> overlay, uint arrayAddress, uint overlayBase,
        int maxEntries = MaxEntries)
    {
        if (arrayAddress < overlayBase)
            return 0;
        long baseOffset = (long)arrayAddress - overlayBase;
        if (baseOffset < 0 || baseOffset >= overlay.Length)
            return 0;

        int count = 0;
        for (int i = 0; i < maxEntries; i++)
        {
            long entryOffset = baseOffset + (long)i * FieldEncounterDecoder.FieldBattlesStride;
            if (entryOffset + FieldEncounterDecoder.FieldBattlesStride > overlay.Length)
                break;

            if (!IsValidFieldBattlesEntry(overlay, (int)entryOffset, overlayBase))
                break;
            count++;
        }
        return count;
    }


    private static bool IsValidFieldBattlesEntry(ReadOnlySpan<byte> overlay, int offset, uint overlayBase)
    {
        if (offset + FieldEncounterDecoder.FieldBattlesStride > overlay.Length)
            return false;
        // pad8 at offset+8 must be zero.
        int pad8 = BinaryPrimitives.ReadInt32LittleEndian(overlay.Slice(offset + 8, 4));
        if (pad8 != 0)
            return false;
        // All four BattleList pointers must be valid.
        for (int i = 0; i < 4; i++)
        {
            uint ptr = BinaryPrimitives.ReadUInt32LittleEndian(overlay.Slice(offset + 12 + i * 4, 4));
            if (!IsValidBattleListPointer(overlay, ptr, overlayBase))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Returns true when <paramref name="ptr"/> resolves to a structurally valid BattleList:
    /// pointer in overlay range, count in [0, BattleRatesCount), and all 8 AreaBattle pointers
    /// also within overlay range.
    /// </summary>
    private static bool IsValidBattleListPointer(ReadOnlySpan<byte> overlay, uint ptr, uint overlayBase)
    {
        if (ptr < overlayBase)
            return false;
        long offset = (long)ptr - overlayBase;
        if (offset + FieldEncounterDecoder.BattleListStride > overlay.Length)
            return false;
        int count = BinaryPrimitives.ReadInt32LittleEndian(overlay.Slice((int)offset, 4));
        if (count < 0 || count >= FieldEncounterDecoder.BattleRatesCount)
            return false;
        // Check all 8 AreaBattle pointers are within range.
        for (int i = 0; i < 8; i++)
        {
            uint aPtr = BinaryPrimitives.ReadUInt32LittleEndian(overlay.Slice((int)offset + 4 + i * 4, 4));
            if (aPtr < overlayBase)
                return false;
            long aOffset = (long)aPtr - overlayBase;
            if (aOffset + FieldEncounterDecoder.AreaBattleStride > overlay.Length)
                return false;
        }
        return true;
    }
}
