namespace Dw3.Formats.Field;

/// <summary>MIPS instruction pattern tests shared by TMPK overlay readers.</summary>
internal static class TmpkMipsOps
{
    /// <summary>Returns true when the word zeroes register 4 (a0) via addiu or sll.</summary>
    /// <param name="word">Raw MIPS instruction word.</param>
    /// <returns>True when the instruction is an a0-zeroing addiu or sll.</returns>
    public static bool IsZeroA0(uint word) =>
        IsAddiu(word, 4, 0, 0) ||
        ((word >> 26) == 0 && ((word >> 21) & 31) == 0 && ((word >> 11) & 31) == 4 && (word & 0x3F) == 0x21);

    /// <summary>Returns true when the word is addiu with the exact immediate.</summary>
    /// <param name="word">Raw MIPS instruction word.</param>
    /// <param name="target">Expected destination register index.</param>
    /// <param name="source">Expected source register index.</param>
    /// <param name="immediate">Expected signed 16-bit immediate value.</param>
    /// <returns>True when the instruction matches addiu with the given operands.</returns>
    public static bool IsAddiu(uint word, int target, int source, short immediate) =>
        (word >> 26) == 9 && ((word >> 16) & 31) == target && ((word >> 21) & 31) == source && (short)word == immediate;

    /// <summary>Returns true when the word is addiu from source to target (any immediate).</summary>
    /// <param name="word">Raw MIPS instruction word.</param>
    /// <param name="target">Expected destination register index.</param>
    /// <param name="source">Expected source register index.</param>
    /// <returns>True when the instruction is addiu between the given registers.</returns>
    public static bool IsAddiuToRegister(uint word, int target, int source) =>
        (word >> 26) == 9 && ((word >> 16) & 31) == target && ((word >> 21) & 31) == source;

    /// <summary>Returns true when the word is lui writing target; immediate is the upper 16-bit constant.</summary>
    /// <param name="word">Raw MIPS instruction word.</param>
    /// <param name="target">Expected destination register index.</param>
    /// <param name="immediate">Receives the upper 16-bit immediate on match.</param>
    /// <returns>True when the instruction is lui targeting the given register.</returns>
    public static bool IsLui(uint word, int target, out ushort immediate)
    {
        immediate = (ushort)word;
        return (word >> 26) == 15 && ((word >> 16) & 31) == target;
    }

    /// <summary>Returns true when the word is ori from source to target; immediate is the lower constant.</summary>
    /// <param name="word">Raw MIPS instruction word.</param>
    /// <param name="target">Expected destination register index.</param>
    /// <param name="source">Expected source register index.</param>
    /// <param name="immediate">Receives the 16-bit immediate on match.</param>
    /// <returns>True when the instruction is ori between the given registers.</returns>
    public static bool IsOri(uint word, int target, int source, out ushort immediate)
    {
        immediate = (ushort)word;
        return (word >> 26) == 13 && ((word >> 16) & 31) == target && ((word >> 21) & 31) == source;
    }

    /// <summary>Returns true when the word is jalr using the given source register.</summary>
    /// <param name="word">Raw MIPS instruction word.</param>
    /// <param name="source">Expected source register holding the call target address.</param>
    /// <returns>True when the instruction is jalr sourced from the given register.</returns>
    public static bool IsJalr(uint word, int source) =>
        (word >> 26) == 0 && ((word >> 21) & 31) == source && (word & 0x3F) == 9;

    /// <summary>Returns true when the word is lw with the exact displacement.</summary>
    /// <param name="word">Raw MIPS instruction word.</param>
    /// <param name="target">Expected destination register index.</param>
    /// <param name="source">Expected base register index.</param>
    /// <param name="displacement">Expected unsigned 16-bit byte displacement.</param>
    /// <returns>True when the instruction is lw with the given operands.</returns>
    public static bool IsLoadWord(uint word, int target, int source, ushort displacement) =>
        (word >> 26) == 35 && ((word >> 16) & 31) == target && ((word >> 21) & 31) == source && (ushort)word == displacement;

    /// <summary>Returns true when the word is sw with the exact displacement.</summary>
    /// <param name="word">Raw MIPS instruction word.</param>
    /// <param name="source">Expected source register index (value stored).</param>
    /// <param name="target">Expected base register index.</param>
    /// <param name="displacement">Expected unsigned 16-bit byte displacement.</param>
    /// <returns>True when the instruction is sw with the given operands.</returns>
    public static bool IsStoreWord(uint word, int source, int target, ushort displacement) =>
        (word >> 26) == 43 && ((word >> 16) & 31) == source && ((word >> 21) & 31) == target && (ushort)word == displacement;

    /// <summary>Returns true when the word is any branch, jump or jalr/jr instruction.</summary>
    /// <param name="word">Raw MIPS instruction word.</param>
    /// <returns>True when the instruction transfers control flow.</returns>
    public static bool IsControlFlow(uint word)
    {
        int opcode = (int)(word >> 26);
        return opcode is 1 or 2 or 3 or 4 or 5 or 6 or 7 or 20 or 21 or 22 or 23 or 29 ||
            (opcode == 0 && (word & 0x3F) is 8 or 9);
    }

    /// <summary>Throws when the word is control flow that is not allowed at this position.</summary>
    /// <param name="word">Raw MIPS instruction word.</param>
    /// <param name="index">Instruction index used in the error message.</param>
    /// <param name="allowJalr">When true, jalr is not rejected.</param>
    /// <exception cref="FormatException">The instruction is a disallowed control-flow instruction.</exception>
    public static void RejectControlFlow(uint word, int index, bool allowJalr = false)
    {
        int opcode = (int)(word >> 26);
        bool jalr = opcode == 0 && (word & 0x3F) == 9;
        if (opcode is 1 or 2 or 3 or 4 or 5 or 6 or 7 or 20 or 21 or 22 or 23 or 29 || (!allowJalr && jalr) || (opcode == 0 && (word & 0x3F) == 8))
        {
            throw new FormatException($"Unsupported MIPS control flow at overlay word {index}.");
        }
    }
}
