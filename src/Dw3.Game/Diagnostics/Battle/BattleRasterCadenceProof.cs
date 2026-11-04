using System.Diagnostics;
using System.Text.Json;
using Dw3Production.Logging;
using Godot;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>Measures a screenshot-free production presentation window without treating capture stalls as cadence.</summary>
internal static class BattleRasterCadenceProof
{
    /// <summary>Records frame cadence and managed allocation under the current offscreen driver and resource limits.</summary>
    /// <param name="host">Production host whose normal battle simulation is held by the diagnostic harness.</param>
    /// <param name="root">Directory receiving machine-readable, qualified measurements.</param>
    /// <returns>A task completed after warm-up and 180 measured rendered frames.</returns>
    public static async Task RunAsync(IBattleProbeHost host, string root)
    {
        var renderer = host.Renderer;
        renderer.BuildModels(host.ResolveBattleModelPath(383), host.ResolveBattleModelPath(51), "fighter-383", "fighter-51", 51);
        renderer.SetProbePose(10);
        renderer.SetProbeAnimationFrame(10);
        renderer.SetCommandMenuCamera();
        for (var i = 0; i < 60; i++)
        {
            renderer.Present(host.BuildBattleView());
            await host.NextFrameDrawn();
        }

        var intervals = new double[180];
        var cpu = new double[180];
        var allocations = GC.GetTotalAllocatedBytes(true);
        var gen0 = GC.CollectionCount(0);
        long presentAllocations = 0;
        for (var i = 0; i < intervals.Length; i++)
        {
            var start = Stopwatch.GetTimestamp();
            var before = GC.GetAllocatedBytesForCurrentThread();
            renderer.Present(host.BuildBattleView());
            presentAllocations += GC.GetAllocatedBytesForCurrentThread() - before;
            cpu[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            await host.NextFrameDrawn();
            intervals[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }

        var totalAllocations = GC.GetTotalAllocatedBytes(true) - allocations;
        using var viewport = host.CaptureViewport();
        var result = new
        {
            workload = "production Present and rendered main+raw frames, source pose10 held, natural383/51 placement, no screenshots/readbacks, not full gameplay",
            driver = RenderingServer.GetVideoAdapterName(),
            viewport = new[] { viewport.GetWidth(), viewport.GetHeight() },
            rawViewport = new[] { 100, 60 },
            samples = intervals.Length,
            frameMeanMs = intervals.Average(),
            frameMedianMs = intervals.Order().ElementAt(intervals.Length / 2),
            frameP95Ms = intervals.Order().ElementAt((int)(intervals.Length * 0.95)),
            presentMeanMs = cpu.Average(),
            managedProcessBytesPerFrame = totalAllocations / intervals.Length,
            presentThreadBytesPerFrame = presentAllocations / intervals.Length,
            gen0Collections = GC.CollectionCount(0) - gen0,
            intervals,
            cpu,
        };
        await File.WriteAllTextAsync(Path.Combine(root, "cadence.json"), JsonSerializer.Serialize(result));
        QuietLog.Info($"battle-cadence qualified-static-probe frames={intervals.Length} frameMeanMs={intervals.Average():0.000} presentMeanMs={cpu.Average():0.000} managedProcessBytesPerFrame={totalAllocations / intervals.Length} presentThreadBytesPerFrame={presentAllocations / intervals.Length} driver={RenderingServer.GetVideoAdapterName()} no-screenshot-window=true not-60fps-acceptance");
    }
}
