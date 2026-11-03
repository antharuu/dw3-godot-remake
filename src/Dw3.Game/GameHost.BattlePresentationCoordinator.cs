using Dw3Production.Logging;
using Dw3Production.Simulation;

namespace Dw3Production;

/// <summary>Contains GameHost's battle-presentation coordination implementation.</summary>
public partial class GameHost
{
    /// <summary>Refreshes battle fighter models when the active pair changes.</summary>
    private sealed class BattlePresentationCoordinator
    {
        private readonly GameHost _host;

        /// <summary>Initializes a new instance of the <see cref="BattlePresentationCoordinator"/> class.</summary>
        /// <param name="host">Runtime host that owns the battle renderer and flow.</param>
        public BattlePresentationCoordinator(GameHost host)
        {
            _host = host;
        }

        /// <summary>Replaces visible models only after a non-empty active fighter id changes.</summary>
        public void RefreshModels()
        {
            BattleFlowController? flow = _host._battleFlow;
            if (flow is null || _host._battleRenderer is null)
            {
                return;
            }

            int playerId = flow.State.ActivePlayerFighter?.Id ?? 0;
            int enemyId = flow.State.ActiveEnemyFighter?.Id ?? 0;
            if (playerId == 0 || enemyId == 0 || (playerId == _host._battleRenderedPlayerId && enemyId == _host._battleRenderedEnemyId))
            {
                return;
            }

            _host._battleRenderer.BuildModels(
                _host.ResolveBattleModelPath(playerId),
                _host.ResolveBattleModelPath(enemyId),
                $"fighter-{playerId}",
                $"fighter-{enemyId}",
                enemyId);
            ApplyPendingChangeMotion(playerId);
            _host._battleRenderedPlayerId = playerId;
            _host._battleRenderedEnemyId = enemyId;
            QuietLog.Info($"battle-model-replaced player={playerId} enemy={enemyId}");
        }

        /// <summary>Applies the recovered normal switch-in motion when a successful switch requested it.</summary>
        /// <param name="playerId">Incoming active player fighter id.</param>
        private void ApplyPendingChangeMotion(int playerId)
        {
            if (_host._battlePendingChangeMotion == 0)
            {
                return;
            }

            int motion = _host._battlePendingChangeMotion;
            _host._battlePendingChangeMotion = 0;
            if (!_host._battleRenderer!.TrySetPlayerMotion(motion, restart: true))
            {
                _host._log.Warn("battle-change-motion-unwired", $"fighter={playerId} motion={motion}");
            }
        }
    }
}
