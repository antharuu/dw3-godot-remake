using System.Text.Json;
using Dw3Production.Diagnostics.Battle;
using Xunit;

namespace Dw3.Game.Tests;

public sealed class BattleStageActionProofTests
{
    [Theory]
    [InlineData("{\"1\":[[[0,1,2,3],[100,1,2,3]]]}", false)]
    [InlineData("{\"1\":[[[0,1,2,3],[100,2,2,3]]]}", true)]
    [InlineData("{}", false)]
    public void DetectsGeometryRatherThanTimestamps(string json, bool animated)
    {
        using var data = JsonDocument.Parse(json);
        Assert.Equal(animated, BattleStageActionProof.HasAnimatedTracks(data.RootElement));
    }
}
