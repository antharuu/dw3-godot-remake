using Dw3.Data.Battle;

namespace Dw3Production.Simulation;

/// <summary>Validates the bounded first-lot grammar before playback can have side effects.</summary>
public static class BattleActionValidation
{
    /// <summary>Checks every command and the explicit final terminator.</summary>
    /// <param name="script">Decoded script to validate.</param>
    /// <exception cref="FormatException">A command is unsupported, malformed or unterminated.</exception>
    public static void Validate(BattleActionScript script)
    {
        ArgumentNullException.ThrowIfNull(script);
        if (script.Commands.Count == 0 || !IsEnd(script.Commands[^1].Opcode))
        {
            throw new FormatException("Action script requires a final End command.");
        }

        for (int i = 0; i < script.Commands.Count; i++)
        {
            var command = script.Commands[i];
            int expected = ArgumentCount(command);
            if (expected < 0 || command.Arguments.Count != expected ||
                (IsEnd(command.Opcode) && i != script.Commands.Count - 1))
            {
                throw new FormatException($"Unsupported or malformed action command {i}: {command.Opcode}.");
            }
        }
    }

    /// <summary>Identifies the two explicit script terminators.</summary>
    /// <param name="opcode">Command opcode.</param>
    /// <returns>Whether the command terminates the script.</returns>
    private static bool IsEnd(BattleActionOpcode opcode) => opcode is BattleActionOpcode.End or BattleActionOpcode.AlternateEnd;

    /// <summary>Returns the qualified operand width, or minus one for unsupported commands.</summary>
    /// <param name="command">Command to inspect.</param>
    /// <returns>Exact operand count.</returns>
    private static int ArgumentCount(BattleActionCommand command)
    {
        var a = command.Arguments;
        return command.Opcode switch
        {
            BattleActionOpcode.End or BattleActionOpcode.AlternateEnd => 0,
            BattleActionOpcode.HitDispatch when a is [>= 0 and <= 5] => 1,
            BattleActionOpcode.Wait when a is [>= 0] => 1,
            BattleActionOpcode.Sound when a is [>= 0, >= 0] => 2,
            BattleActionOpcode.Fade when a is [0 or 1, >= 0] => 2,
            BattleActionOpcode.Model => ModelCount(a),
            BattleActionOpcode.SpriteEffect => FamilyCount(a, 5, 2),
            BattleActionOpcode.EffectModel => FamilyCount(a, 8, 2),
            BattleActionOpcode.Stage => FamilyCount(a, 4, 2),
            BattleActionOpcode.Camera => CameraCount(a),
            _ => -1,
        };
    }

    /// <summary>Checks the exact supported widths of fighter lifecycle and movement commands.</summary>
    /// <param name="arguments">Source model operands.</param>
    /// <returns>Qualified width, or minus one.</returns>
    private static int ModelCount(IReadOnlyList<short> arguments) => arguments switch
    {
        [0, >= 0 and <= 6, >= 0] => 3,
        [1 or 2, >= 0 and <= 6] => 2,
        [9, 2 or 3] => 2,
        [6, 2 or 3, > 0] => 3,
        [4, >= 0 and <= 4, _, _, _, >= 0] => 6,
        [3, >= 0 and <= 6, _, _, _, >= 0] => 6,
        [7 or 8, >= 0 and <= 6, >= 0] => 3,
        [5, >= 0 and <= 6, 0 or 1 or 2 or 3 or 5 or 6] => 3,
        [5, >= 0 and <= 6, 4, _] => 4,
        _ => -1,
    };

    /// <summary>Validates preset range and positive projection before requesting a physical camera.</summary>
    /// <param name="arguments">Camera operands.</param>
    /// <returns>Width, or minus one for an invalid view.</returns>
    private static int CameraCount(IReadOnlyList<short> arguments)
    {
        if (arguments is [>= 0, 0, >= 0 and <= 7])
        {
            return 3;
        }

        return arguments.Count == 16 && arguments[0] >= 0 && arguments[1] == 1 && arguments[15] > 0 ? 16 : -1;
    }

    /// <summary>Gets a width for the qualified load/start subcommands.</summary>
    /// <param name="arguments">Complete operands.</param>
    /// <param name="start">Width of subcommand zero.</param>
    /// <param name="load">Width of subcommand one.</param>
    /// <returns>Exact width or minus one.</returns>
    private static int FamilyCount(IReadOnlyList<short> arguments, int start, int load) =>
        arguments.Count == 0 ? -1 : arguments[0] switch { 0 => start, 1 => load, _ => -1 };
}
