using Dw3Production;
using Dw3Production.Diagnostics;
using Xunit;

namespace Dw3.Game.Tests;

public sealed class DiagnosticProbeFamilyRunnerTests
{
    [Fact]
    public void BattleRunnerPreservesBattleProbePrecedence()
    {
        var host = new RecordingProbeHost();
        var options = HostStartupOptions.From(name =>
            name is "DW3_BATTLE_HUD_PROBE" or "DW3_BATTLE_PLAY_PROBE" ? "1" : null);

        bool dispatched = new BattleDiagnosticProbeRunner(host).TryDispatch(options);

        Assert.True(dispatched);
        Assert.Equal("RunBattleHudProbe", host.MethodName);
        Assert.True(host.OwnsInput);
    }

    [Fact]
    public void BattleRunnerSelectsActionProbe()
    {
        var host = new RecordingProbeHost();
        var options = HostStartupOptions.From(name => name == "DW3_BATTLE_ACTION_PROBE" ? "1" : null);

        bool dispatched = new BattleDiagnosticProbeRunner(host).TryDispatch(options);

        Assert.True(dispatched);
        Assert.Equal("RunBattleActionProbe", host.MethodName);
        Assert.True(host.OwnsInput);
    }

    [Fact]
    public void DialogueRunnerOwnsDialogueAndIconProbeSelection()
    {
        var host = new RecordingProbeHost();
        var options = HostStartupOptions.From(name =>
            name is "DW3_DIALOGUE_PROBE" or "DW3_ICON_PROBE" ? "1" : null);

        bool dispatched = new DialogueDiagnosticProbeRunner(host).TryDispatch(options);

        Assert.True(dispatched);
        Assert.Equal("RunDialogueProbe", host.MethodName);
        Assert.True(host.OwnsInput);
    }

    [Fact]
    public void StorageRunnerSelectsSqliteProbe()
    {
        var host = new RecordingProbeHost();
        var options = HostStartupOptions.From(name => name == "DW3_SQLITE_PROBE" ? "1" : null);

        bool dispatched = new StorageDiagnosticProbeRunner(host).TryDispatch(options);

        Assert.True(dispatched);
        Assert.Equal("RunSqliteProbe", host.MethodName);
        Assert.True(host.OwnsInput);
    }

    private sealed class RecordingProbeHost : IDiagnosticProbeHost
    {
        public string? MethodName { get; private set; }

        public bool OwnsInput { get; private set; }

        public void RunInputProbeAndQuit() => throw new InvalidOperationException();

        public void ScheduleProbe(string methodName, bool ownsInput)
        {
            MethodName = methodName;
            OwnsInput = ownsInput;
        }
    }
}
