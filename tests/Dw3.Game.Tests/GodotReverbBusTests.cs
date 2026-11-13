using Dw3Production.Presentation;
using Xunit;

namespace Dw3.Game.Tests;

public sealed class GodotReverbBusTests
{
    [Fact]
    public void RoomSize_FollowsTheDocumentedSpuWorkAreaOrder()
    {
        float[] ordered = [GodotReverbBus.RoomSize(2), GodotReverbBus.RoomSize(1), GodotReverbBus.RoomSize(3), GodotReverbBus.RoomSize(4), GodotReverbBus.RoomSize(5), GodotReverbBus.RoomSize(6)];

        Assert.Equal(ordered.Order(), ordered);
        Assert.Equal(0, GodotReverbBus.RoomSize(0));
        Assert.Equal(1, GodotReverbBus.RoomSize(6));
        Assert.InRange(GodotReverbBus.RoomSize(GodotReverbBus.InitialType), 0.29f, 0.30f);
    }

    [Theory]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(10)]
    public void RoomSize_EchoDelayOrUnknownPreset_HasNoMapping(int type)
    {
        Assert.Throws<NotSupportedException>(() => GodotReverbBus.RoomSize(type));
    }
}
