using System.Text.Json;
using Dw3Production.Conversion;
using Xunit;

namespace Dw3.Game.Tests;

public sealed class BattleActionManifestValidationTests
{
    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    public void MalformedRootIsAFormatFailure(string json) =>
        Assert.Throws<FormatException>(() => BattleActionScriptJsonParser.Parse(json));

    [Theory]
    [InlineData(123, "[]")]
    [InlineData(2, "[0]")]
    [InlineData(5, "[0,0,9]")]
    [InlineData(5, "[0,1,0,0,0,0,0,0,0,0,0,0,0,0,0,0]")]
    [InlineData(10, "[1,-1]")]
    [InlineData(5, "[0,1,0,0,0,0,0,0,0,0,0,0,0,0,1,300]")]
    public void UnsupportedOrInvalidOperandsRejectBeforePlayback(int opcode, string arguments)
    {
        string json = JsonSerializer.Serialize(new
        {
            schema = "m9b-battle-actions-v3",
            scriptSelector = 5,
            commands = new[]
            {
                new { opcode, arguments = JsonSerializer.Deserialize<short[]>(arguments) ?? [] },
                new { opcode = 255, arguments = Array.Empty<short>() },
            },
        });
        Assert.Throws<FormatException>(() => BattleActionScriptJsonParser.Parse(json));
    }
}
