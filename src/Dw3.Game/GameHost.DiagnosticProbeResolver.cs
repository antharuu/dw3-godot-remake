using Dw3Production.Diagnostics;
using Dw3Production.Diagnostics.Battle;
using Dw3Production.Diagnostics.Field;

namespace Dw3Production;

public partial class GameHost
{
    /// <summary>Resolves named diagnostic probe entry points across the established probe families.</summary>
    private sealed class DiagnosticProbeResolver
    {
        private readonly GameHost _host;

        /// <summary>Initializes probe resolution for one host session.</summary>
        /// <param name="host">Runtime host that owns probe callbacks.</param>
        public DiagnosticProbeResolver(GameHost host)
        {
            _host = host;
        }

        /// <summary>Returns the asynchronous body registered for an entry name.</summary>
        /// <param name="entryName">Entry name selected by a diagnostic runner.</param>
        /// <returns>Probe body to start after the current frame.</returns>
        /// <exception cref="InvalidOperationException">No probe family registers the entry name.</exception>
        public Func<Task> Resolve(string entryName)
        {
            var fieldProbe = FieldProbeCatalog.Create(entryName, _host) ?? DialogueProbeCatalog.Create(entryName, _host);
            if (fieldProbe is not null)
            {
                return fieldProbe.RunAsync;
            }

            var battleProbe = BattleProbeCatalog.Create(entryName, new BattleProbeContext(_host));
            if (battleProbe is not null)
            {
                return battleProbe.RunAsync;
            }

            if (entryName == "RunSqliteProbe")
            {
                return RunSqliteProbe;
            }

            throw new InvalidOperationException($"no diagnostic probe is registered as {entryName}");
        }

        /// <summary>Runs the storage probe and completes its host process with its result.</summary>
        /// <returns>Completed storage-probe task.</returns>
        private Task RunSqliteProbe()
        {
            SqliteProbe.Run(code => _host.GetTree().Quit(code));
            return Task.CompletedTask;
        }
    }
}
