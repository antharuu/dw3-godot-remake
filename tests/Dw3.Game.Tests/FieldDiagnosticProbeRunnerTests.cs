using Dw3Production;
using Dw3Production.Diagnostics;
using Xunit;

namespace Dw3.Game.Tests;

public sealed class FieldDiagnosticProbeRunnerTests
{
    [Fact]
    public void DispatchesFirstEnabledFieldProbeWithOriginalDriverPolicy()
    {
        var host = new RecordingProbeHost();
        var options = HostStartupOptions.From(name =>
            name is "DW3_LOCOMOTION_PROBE" or "DW3_F3_PROBE" ? "1" : null);

        bool dispatched = new FieldDiagnosticProbeRunner(host).TryDispatch(options);

        Assert.True(dispatched);
        Assert.Equal("RunLocomotionProbe", host.MethodName);
        Assert.False(host.OwnsInput);
        Assert.False(host.InputProbeRan);
    }

    [Fact]
    public void InputProbeUsesImmediateHostEntryPoint()
    {
        var host = new RecordingProbeHost();
        var options = HostStartupOptions.From(name => name == "DW3_INPUT_PROBE" ? "1" : null);

        bool dispatched = new FieldDiagnosticProbeRunner(host).TryDispatch(options);

        Assert.True(dispatched);
        Assert.True(host.InputProbeRan);
        Assert.Null(host.MethodName);
    }

    [Fact]
    public void ReturnsFalseWhenNoFieldProbeIsEnabled()
    {
        var host = new RecordingProbeHost();

        bool dispatched = new FieldDiagnosticProbeRunner(host).TryDispatch(HostStartupOptions.From(_ => null));

        Assert.False(dispatched);
        Assert.False(host.InputProbeRan);
        Assert.Null(host.MethodName);
    }

    private sealed class RecordingProbeHost : IDiagnosticProbeHost
    {
        public string? MethodName { get; private set; }
        public bool OwnsInput { get; private set; }
        public bool InputProbeRan { get; private set; }

        public void RunInputProbeAndQuit() => InputProbeRan = true;

        public void ScheduleProbe(string methodName, bool ownsInput)
        {
            MethodName = methodName;
            OwnsInput = ownsInput;
        }
    }
}
