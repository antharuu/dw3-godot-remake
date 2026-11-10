using System.Buffers.Binary;

namespace Dw3.Formats.Battle;

/// <summary>Decodes the established motion, sprite-effect, wait and end commands of fighter action archives.</summary>
public static class BattleActionScriptDecoder
{
    private const int EndOpcode = 0;
    private const int ModelOpcode = 2;
    private const int SpriteEffectOpcode = 3;
    private const int WaitOpcode = 11;

    /// <summary>Decodes one archive entry without reading outside the entry's declared bounds.</summary>
    /// <param name="archive">Raw u32-offset archive data.</param>
    /// <param name="entry">Selected archive entry.</param>
    /// <returns>The decoded action script.</returns>
    /// <exception cref="FormatException">The archive, entry or command boundary is invalid.</exception>
    public static BattleActionScript Decode(byte[] archive, int entry)
    {
        ArgumentNullException.ThrowIfNull(archive);
        (int start, int end) = ReadEntryBounds(archive, entry);
        var commands = new List<BattleActionCommand>();
        int cursor = start;
        while (cursor < end)
        {
            short rawOpcode = ReadWord(archive, ref cursor, end, entry, "opcode");
            BattleActionCommand command = ReadCommand(archive, rawOpcode, ref cursor, end, entry);
            commands.Add(command);
            if (command.Opcode == BattleActionOpcode.End)
            {
                return new BattleActionScript(entry, commands);
            }
        }

        throw new FormatException($"battle action script entry {entry} has no end command");
    }

    /// <summary>Maps one raw opcode and consumes its subcommand-specific argument words.</summary>
    /// <param name="data">Complete archive data.</param>
    /// <param name="raw">Raw signed opcode word.</param>
    /// <param name="cursor">Cursor advanced beyond the command.</param>
    /// <param name="end">Exclusive boundary of this archive entry.</param>
    /// <param name="entry">Archive-entry index for error messages.</param>
    /// <returns>The semantic command.</returns>
    private static BattleActionCommand ReadCommand(byte[] data, short raw, ref int cursor, int end, int entry) => raw switch
    {
        EndOpcode => new BattleActionCommand(BattleActionOpcode.End, []),
        ModelOpcode => ReadModelCommand(data, ref cursor, end, entry),
        SpriteEffectOpcode => ReadSpriteEffectCommand(data, ref cursor, end, entry),
        WaitOpcode => new BattleActionCommand(BattleActionOpcode.Wait, ReadArguments(data, ref cursor, end, entry, raw, 1)),
        _ => throw new FormatException($"battle action script entry {entry} has unsupported opcode 0x{raw:X}"),
    };

    /// <summary>Reads the qualified motion and visibility forms of model opcode two.</summary>
    /// <param name="data">Complete archive data.</param>
    /// <param name="cursor">Cursor advanced beyond the command.</param>
    /// <param name="end">Exclusive boundary of this archive entry.</param>
    /// <param name="entry">Archive-entry index for error messages.</param>
    /// <returns>The semantic model command.</returns>
    private static BattleActionCommand ReadModelCommand(byte[] data, ref int cursor, int end, int entry)
    {
        short subcommand = ReadWord(data, ref cursor, end, entry, "model subcommand");
        short actor = ReadWord(data, ref cursor, end, entry, "model actor");
        return subcommand switch
        {
            0 => new BattleActionCommand(BattleActionOpcode.Model, [subcommand, actor, ReadWord(data, ref cursor, end, entry, "model motion")]),
            1 => new BattleActionCommand(BattleActionOpcode.Model, [subcommand, actor]),
            _ => throw new FormatException($"battle action script entry {entry} has unsupported model subcommand {subcommand}"),
        };
    }

    /// <summary>Reads the qualified start and load forms of sprite-effect opcode three.</summary>
    /// <param name="data">Complete archive data.</param>
    /// <param name="cursor">Cursor advanced beyond the command.</param>
    /// <param name="end">Exclusive boundary of this archive entry.</param>
    /// <param name="entry">Archive-entry index for error messages.</param>
    /// <returns>The semantic sprite-effect command.</returns>
    private static BattleActionCommand ReadSpriteEffectCommand(byte[] data, ref int cursor, int end, int entry)
    {
        short subcommand = ReadWord(data, ref cursor, end, entry, "sprite-effect subcommand");
        short effect = ReadWord(data, ref cursor, end, entry, "sprite-effect reference");
        return subcommand switch
        {
            0 => new BattleActionCommand(BattleActionOpcode.SpriteEffect, [subcommand, effect, .. ReadArguments(data, ref cursor, end, entry, SpriteEffectOpcode, 3)]),
            1 => new BattleActionCommand(BattleActionOpcode.SpriteEffect, [subcommand, effect]),
            _ => throw new FormatException($"battle action script entry {entry} has unsupported sprite-effect subcommand {subcommand}"),
        };
    }

    /// <summary>Reads the exact fixed count of signed 16-bit arguments for one command.</summary>
    /// <param name="data">Complete archive data.</param>
    /// <param name="cursor">Cursor advanced beyond the argument list.</param>
    /// <param name="end">Exclusive boundary of this archive entry.</param>
    /// <param name="entry">Archive-entry index for error messages.</param>
    /// <param name="opcode">Raw opcode that owns these arguments.</param>
    /// <param name="count">Expected word count.</param>
    /// <returns>The source-order argument words.</returns>
    private static short[] ReadArguments(byte[] data, ref int cursor, int end, int entry, short opcode, int count)
    {
        var arguments = new short[count];
        for (int index = 0; index < count; index++)
        {
            arguments[index] = ReadWord(data, ref cursor, end, entry, $"command 0x{opcode:X}");
        }

        return arguments;
    }

    /// <summary>Reads one word and refuses a command that crosses its archive-entry boundary.</summary>
    /// <param name="data">Complete archive data.</param>
    /// <param name="cursor">Cursor advanced by one word after a successful read.</param>
    /// <param name="end">Exclusive boundary of this archive entry.</param>
    /// <param name="entry">Archive-entry index for error messages.</param>
    /// <param name="command">Command wording for the error.</param>
    /// <returns>The signed 16-bit source word.</returns>
    private static short ReadWord(byte[] data, ref int cursor, int end, int entry, string command)
    {
        if (cursor + sizeof(short) > end)
        {
            throw new FormatException($"battle action script entry {entry} {command} is truncated");
        }

        short value = BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(cursor, sizeof(short)));
        cursor += sizeof(short);
        return value;
    }

    /// <summary>Gets one present archive range through the shared validated offset-table grammar.</summary>
    /// <param name="archive">Complete action archive.</param>
    /// <param name="entry">Selected entry index.</param>
    /// <returns>The selected non-empty start and exclusive-end range.</returns>
    private static (int Start, int End) ReadEntryBounds(byte[] archive, int entry)
    {
        IReadOnlyList<ArchiveRange> bounds = ModelArchive.ReadBounds(archive);
        if (entry < 0 || entry >= bounds.Count)
        {
            throw new FormatException($"battle action archive entry {entry} is outside its offset table");
        }

        ArchiveRange range = bounds[entry];
        if (range.Start == range.End)
        {
            throw new FormatException($"battle action archive entry {entry} is absent");
        }

        return (range.Start, range.End);
    }
}
