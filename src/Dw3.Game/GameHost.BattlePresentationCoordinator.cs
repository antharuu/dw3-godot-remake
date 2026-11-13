using Dw3Production.Logging;
using Dw3Production.Presentation;
using Dw3Production.Simulation;

namespace Dw3Production;

/// <summary>Contains GameHost's battle-presentation coordination implementation.</summary>
public partial class GameHost
{
    /// <summary>Refreshes battle fighter models when the active pair changes, through the source entrance when another
    /// party slot or a new enemy comes in.</summary>
    private sealed class BattlePresentationCoordinator
    {
        private readonly GameHost _host;
        private BattleEntranceSequence? _entrance;
        private int _outgoingId;
        private int _renderedPlayerSlot = -1;

        /// <summary>Initializes a new instance of the <see cref="BattlePresentationCoordinator"/> class.</summary>
        /// <param name="host">Runtime host that owns the battle renderer and flow.</param>
        public BattlePresentationCoordinator(GameHost host)
        {
            _host = host;
        }

        /// <summary>Gets the playing entrance, for private offscreen proofs.</summary>
        public BattleEntranceSequence? Entrance => _entrance;

        /// <summary>Replaces visible models only after a non-empty active fighter id changes.</summary>
        public void RefreshModels() => Refresh(allowEntrance: true);

        /// <summary>Builds the first models of a battle at once, drops any interrupted entrance and records the
        /// opening party slot, so only a later change plays an entrance.</summary>
        public void RefreshModelsForBattleStart()
        {
            Cancel();
            Refresh(allowEntrance: false);
            _renderedPlayerSlot = _host._battleFlow?.State.ActivePlayer ?? -1;
        }

        /// <summary>Plays one source frame of the active entrance. The battle flow waits while it plays, as the
        /// source menu waits for its entrance child before its message and the next turn.</summary>
        /// <returns>Whether an entrance owned this frame.</returns>
        public bool TickTransition()
        {
            if (_entrance is null)
            {
                return false;
            }

            if (_host._battleRenderer is null || _host._battleFlow is null)
            {
                Cancel();
                return false;
            }

            _entrance.Step();
            if (_entrance.IsComplete)
            {
                QuietLog.Info($"battle-entrance complete enemy={_entrance.Enemy} frames={_entrance.Frames}");
                Cancel();
            }

            return true;
        }

        /// <summary>Keeps the outgoing fighter's status and hides the flow message while an entrance plays.</summary>
        /// <param name="view">Built battle view.</param>
        /// <returns>The view to present.</returns>
        public BattleView? Present(BattleView? view) => _entrance is { } entrance && view is { } value ? BattleEntranceView.Apply(value, entrance, _outgoingId) : view;

        /// <summary>Replaces visible models after a non-empty active fighter id changes.</summary>
        /// <param name="allowEntrance">Whether the change may play the source entrance.</param>
        private void Refresh(bool allowEntrance)
        {
            BattleFlowController? flow = _host._battleFlow;
            if (_entrance is not null || flow is null || _host._battleRenderer is not { } renderer || flow.Phase == BattleFlowPhase.ActionSequence)
            {
                return;
            }

            int playerId = flow.State.ActivePlayerFighter?.Id ?? 0;
            int enemyId = flow.State.ActiveEnemyFighter?.Id ?? 0;
            if (playerId == 0 || enemyId == 0 || (playerId == _host._battleRenderedPlayerId && enemyId == _host._battleRenderedEnemyId))
            {
                return;
            }

            if (!allowEntrance || !TryBeginEntrance(flow, renderer, playerId, enemyId))
            {
                Rebuild(renderer, flow, playerId, enemyId);
            }
        }

        /// <summary>Drops the entrance state and returns camera and visibility ownership to the battle.</summary>
        private void Cancel()
        {
            _entrance = null;
            _host._battleRenderer?.ActorScene.Active = false;
        }

        /// <summary>Starts the source entrance when another party slot became active (a switch, a forced switch after a
        /// knockout or a pair tag-in) or when a new enemy came in, keeping the outgoing model until the flash covers the
        /// screen. A form change of the same slot (digivolution, blast, return) is not an entrance and keeps the
        /// immediate rebuild until its own change sequence exists.</summary>
        /// <param name="flow">Active battle flow, already mutated.</param>
        /// <param name="renderer">Battle renderer.</param>
        /// <param name="playerId">New active player fighter id.</param>
        /// <param name="enemyId">New active enemy fighter id.</param>
        /// <returns>Whether an entrance started.</returns>
        private bool TryBeginEntrance(BattleFlowController flow, BattleRenderer renderer, int playerId, int enemyId)
        {
            bool playerEnters = flow.State.ActivePlayer != _renderedPlayerSlot && _renderedPlayerSlot >= 0 && enemyId == _host._battleRenderedEnemyId;
            bool enemyEnters = playerId == _host._battleRenderedPlayerId && _host._battleRenderedEnemyId > 0;
            BattleFighter? entering = playerEnters ? flow.State.ActivePlayerFighter : flow.State.ActiveEnemyFighter;
            if ((!playerEnters && !enemyEnters) || entering is null)
            {
                return false;
            }

            int idle = enemyEnters && !OutgoingEnemyKnockedOut(flow)
                ? BattleWeakIdleRule.Normal
                : BattleWeakIdleRule.Select(entering.Hp, 0, entering.MaxHp);
            var stage = new BattleRendererEntranceStage(renderer, enemyEnters, () => Rebuild(renderer, flow, playerId, enemyId), _host._log.Warn);
            renderer.StopShotCamera();
            renderer.ActorScene.Active = true;
            _entrance = new BattleEntranceSequence(enemyEnters, idle, stage);
            _outgoingId = enemyEnters ? _host._battleRenderedEnemyId : _host._battleRenderedPlayerId;
            QuietLog.Info($"battle-entrance begin enemy={enemyEnters} outgoing={_outgoingId} incoming={entering.Id} idle={idle}");
            return true;
        }

        /// <summary>Returns whether the replaced enemy left on a knockout, which makes the source pick the incoming
        /// enemy's idle by its HP. A voluntary enemy switch always enters with the normal idle.</summary>
        /// <param name="flow">Active battle flow.</param>
        /// <returns>Whether the outgoing enemy is knocked out.</returns>
        private bool OutgoingEnemyKnockedOut(BattleFlowController flow) =>
            flow.State.Enemies.Any(enemy => enemy is { IsKnockedOut: true } && enemy.Id == _host._battleRenderedEnemyId);

        /// <summary>Builds the active pair's models and records them as rendered.</summary>
        /// <param name="renderer">Battle renderer.</param>
        /// <param name="flow">Active battle flow, whose party slot is recorded with the rendered player.</param>
        /// <param name="playerId">Active player fighter id.</param>
        /// <param name="enemyId">Active enemy fighter id.</param>
        private void Rebuild(BattleRenderer renderer, BattleFlowController flow, int playerId, int enemyId)
        {
            renderer.BuildModels(
                _host.ResolveBattleModelPath(playerId),
                _host.ResolveBattleModelPath(enemyId),
                $"fighter-{playerId}",
                $"fighter-{enemyId}",
                enemyId);
            _host._battleRenderedPlayerId = playerId;
            _host._battleRenderedEnemyId = enemyId;
            _renderedPlayerSlot = flow.State.ActivePlayer;
            QuietLog.Info($"battle-model-replaced player={playerId} enemy={enemyId}");
        }
    }
}
