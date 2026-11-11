using System.Text.Json;
using Dw3.Data.Battle;

namespace Dw3Production.Conversion;

/// <summary>Parses the <c>m9b-battle-actions-v3</c> JSON manifest into a <see cref="BattleActionScript"/>.</summary>
public static class BattleActionScriptJsonParser
{
    private const string ExpectedSchema = "m9b-battle-actions-v3";

    /// <summary>Reads an action-script manifest from the given JSON text and returns the decoded script.</summary>
    /// <param name="json">Raw JSON text of the manifest.</param>
    /// <param name="context">Human-readable context used in error messages.</param>
    /// <returns>The decoded action script.</returns>
    /// <exception cref="FormatException">The JSON is missing required fields, the schema tag does not match, or a command is malformed.</exception>
    public static BattleActionScript Parse(string json, string context = "action script")
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            using var document = JsonDocument.Parse(json);
            return ParseDocument(document.RootElement, context);
        }
        catch (JsonException error)
        {
            throw new FormatException($"{context}: JSON parse error: {error.Message}", error);
        }
    }

    /// <summary>Reads an action-script manifest from the given file path and returns the decoded script.</summary>
    /// <param name="path">Absolute path to the JSON file.</param>
    /// <returns>The decoded action script.</returns>
    /// <exception cref="FormatException">The file is not valid JSON or the manifest is malformed.</exception>
    /// <exception cref="IOException">The file cannot be read.</exception>
    public static BattleActionScript ParseFile(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (IOException error)
        {
            throw new IOException($"action script file unreadable: {path}: {error.Message}", error);
        }

        return Parse(json, $"action script {path}");
    }

    /// <summary>Parses the validated root element of an action-script manifest.</summary>
    /// <param name="root">Root element of the JSON document.</param>
    /// <param name="context">Human-readable context used in error messages.</param>
    /// <returns>The decoded action script.</returns>
    private static BattleActionScript ParseDocument(JsonElement root, string context)
    {
        if (!root.TryGetProperty("schema", out var schemaProp) ||
            schemaProp.GetString() != ExpectedSchema)
        {
            throw new FormatException($"{context}: schema must be '{ExpectedSchema}'");
        }

        if (!root.TryGetProperty("scriptSelector", out var selectorProp) ||
            !selectorProp.TryGetInt32(out int selector))
        {
            throw new FormatException($"{context}: missing scriptSelector");
        }

        if (!root.TryGetProperty("commands", out var commandsProp) ||
            commandsProp.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException($"{context}: missing commands array");
        }

        var commands = new List<BattleActionCommand>();
        foreach (var commandElement in commandsProp.EnumerateArray())
        {
            commands.Add(ParseCommand(commandElement, context));
        }

        return new BattleActionScript(selector, commands);
    }

    /// <summary>Parses one command object from the commands array.</summary>
    /// <param name="element">The JSON object representing one command.</param>
    /// <param name="context">Human-readable context used in error messages.</param>
    /// <returns>The decoded command.</returns>
    private static BattleActionCommand ParseCommand(JsonElement element, string context)
    {
        if (!element.TryGetProperty("opcode", out var opcodeProp) ||
            !opcodeProp.TryGetInt32(out int rawOpcode))
        {
            throw new FormatException($"{context}: command missing opcode");
        }

        var opcode = (BattleActionOpcode)rawOpcode;

        var arguments = new List<short>();
        if (element.TryGetProperty("arguments", out var argsProp) &&
            argsProp.ValueKind == JsonValueKind.Array)
        {
            foreach (var arg in argsProp.EnumerateArray())
            {
                if (!arg.TryGetInt16(out short value))
                {
                    throw new FormatException($"{context}: command argument is not a valid short");
                }

                arguments.Add(value);
            }
        }

        return new BattleActionCommand(opcode, arguments);
    }
}
