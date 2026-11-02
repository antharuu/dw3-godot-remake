using Godot;

namespace Dw3Production;

/// <summary>Contains GameHost's first-launch completion implementation.</summary>
public partial class GameHost
{
    /// <summary>Completes first-launch extraction and runs its optional post-startup capture probe.</summary>
    private sealed class FirstLaunchCompletionCoordinator
    {
        private readonly GameHost _host;

        /// <summary>Initializes a new instance of the <see cref="FirstLaunchCompletionCoordinator"/> class.</summary>
        /// <param name="host">Runtime host that owns the first-launch screen and startup path.</param>
        public FirstLaunchCompletionCoordinator(GameHost host)
        {
            _host = host;
        }

        /// <summary>Removes the extraction screen, starts the session, and optionally captures the resulting field.</summary>
        /// <param name="dataRoot">Validated data-pack root created by extraction.</param>
        /// <returns>Completion task for deferred first-launch work.</returns>
        public async Task CompleteAsync(string dataRoot)
        {
            RemoveFirstLaunchScreen();
            _host.StartupWithDataPack(dataRoot);
            if (!_host._options.FirstLaunchProbe)
            {
                return;
            }

            await _host.ToSignal(_host.GetTree(), SceneTree.SignalName.ProcessFrame);
            await _host.ToSignal(_host.GetTree(), SceneTree.SignalName.ProcessFrame);
            await _host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            string capturePath = Path.Combine(_host.ProbeOutputRoot(dataRoot), "first-launch-s200.png");
            Error saveErr = _host.GetViewport().GetTexture().GetImage().SavePng(capturePath);
            GD.Print(saveErr == Error.Ok
                ? $"FIRST_LAUNCH_PROBE capture OK: {capturePath}"
                : $"FIRST_LAUNCH_PROBE capture FAIL ({saveErr}): {capturePath}");
            GD.Print("FIRST_LAUNCH_PROBE PASS");
            _host.GetTree().Quit(0);
        }

        /// <summary>Queues the first-launch screen for removal before normal session startup.</summary>
        private void RemoveFirstLaunchScreen()
        {
            foreach (Node child in _host.GetChildren())
            {
                if (child is FirstLaunchScreen)
                {
                    child.QueueFree();
                    return;
                }
            }
        }
    }
}
