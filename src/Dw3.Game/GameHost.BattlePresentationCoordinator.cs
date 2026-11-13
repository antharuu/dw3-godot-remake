using Dw3Production.Logging;
using Dw3Production.Presentation;
using Dw3Production.Simulation;

namespace Dw3Production;

/// <summary>Contains GameHost's battle-presentation coordination implementation.</summary>
public partial class GameHost
{
    /// <summary>Refreshes battle fighter models when the active pair changes, through the source entrance and its
    /// caller's messages when another party slot or another enemy slot comes in.</summary>
    private sealed class BattlePresentationCoordinator
    {
        private readonly GameHost _host;
        private BattleEntranceCall? _call;
        private int _outgoingSlot = -1;
        private int _renderedPlayerSlot = -1;
        private int _renderedEnemySlot = -1;
        private BattleFlowPhase _observedPhase = BattleFlowPhase.PlayerCommand;

        /// <summary>Initializes a new instance of the <see cref="BattlePresentationCoordinator"/> class.</summary>
        /// <param name="host">Runtime host that owns the battle renderer and flow.</param>
        public BattlePresentationCoordinator(GameHost host)
        {
            _host = host;
        }

        /// <summary>Gets the playing entrance, for private offscreen proofs.</summary>
        public BattleEntranceSequence? Entrance => _call?.Sequence;

        /// <summary>Gets the running entrance call with its messages, for private offscreen proofs.</summary>
        public BattleEntranceCall? Call => _call;

        /// <summary>Gets the slots the battle scene shows, for private offscreen proofs.</summary>
        public (int Player, int Enemy) RenderedSlots => (_renderedPlayerSlot, _renderedEnemySlot);

        /// <summary>Gets or sets the Cross source a scripted driver uses for the caller's messages; when unset, a
        /// scripted driver closes each message as soon as its arrow shows.</summary>
        public Func<bool?>? ProbeConfirm { get; set; }

        /// <summary>Replaces visible models only after the active pair changes.</summary>
        public void RefreshModels() => Refresh(allowEntrance: true);

        /// <summary>Builds the first models of a battle at once, drops any interrupted entrance and records the
        /// opening slots, so only a later change plays an entrance.</summary>
        public void RefreshModelsForBattleStart()
        {
            Cancel();
            _renderedPlayerSlot = _host._battleFlow?.State.ActivePlayer ?? -1;
            _renderedEnemySlot = _host._battleFlow?.State.ActiveEnemy ?? -1;
            Refresh(allowEntrance: false);
        }

        /// <summary>Plays one source time unit of the running entrance call. The battle flow waits for the whole call,
        /// as the source caller waits for its message before the next turn.</summary>
        /// <returns>Whether an entrance call owned this unit.</returns>
        public bool TickTransition()
        {
            if (_call is null)
            {
                return false;
            }

            if (_host._battleRenderer is null || _host._battleFlow is not { } flow)
            {
                Cancel();
                return false;
            }

            _call.Step(Confirm());
            if (_call.Phase == BattleEntranceCallPhase.Done)
            {
                QuietLog.Info($"battle-entrance complete enemy={_call.Sequence.Enemy} frames={_call.Sequence.Frames} message={_call.AfterFrames}");
                Release(flow);
            }

            return true;
        }

        /// <summary>Keeps the outgoing fighter's status and shows the caller's message while an entrance call runs.</summary>
        /// <param name="view">Built battle view.</param>
        /// <returns>The view to present.</returns>
        public BattleView? Present(BattleView? view) => _call is { } call && view is { } value ? BattleEntranceView.Apply(value, call, _outgoingSlot) : view;

        /// <summary>Replaces visible models after the active pair changed.</summary>
        /// <param name="allowEntrance">Whether the change may play the source entrance.</param>
        private void Refresh(bool allowEntrance)
        {
            BattleFlowController? flow = _host._battleFlow;
            BattleFlowPhase previous = _observedPhase;
            _observedPhase = flow?.Phase ?? BattleFlowPhase.PlayerCommand;
            if (_call is not null || flow is null || _host._battleRenderer is not { } renderer || flow.Phase == BattleFlowPhase.ActionSequence)
            {
                return;
            }

            int playerId = flow.State.ActivePlayerFighter?.Id ?? 0;
            int enemyId = flow.State.ActiveEnemyFighter?.Id ?? 0;
            if (playerId == 0 || enemyId == 0 || !Changed(flow.State, playerId, enemyId))
            {
                return;
            }

            if (!allowEntrance || !TryBeginEntrance(flow, renderer, previous == BattleFlowPhase.ActionSequence))
            {
                Rebuild(renderer, flow, playerId, enemyId);
            }
        }

        /// <summary>Returns whether the active pair differs from the rendered one, by slot or by fighter id.</summary>
        /// <param name="state">Battle state.</param>
        /// <param name="playerId">Active player fighter id.</param>
        /// <param name="enemyId">Active enemy fighter id.</param>
        /// <returns>Whether the scene must change.</returns>
        private bool Changed(BattleState state, int playerId, int enemyId) =>
            playerId != _host._battleRenderedPlayerId || enemyId != _host._battleRenderedEnemyId
            || state.ActivePlayer != _renderedPlayerSlot || state.ActiveEnemy != _renderedEnemySlot;

        /// <summary>Reads Cross for the caller's message: the player's press, or the scripted driver's.</summary>
        /// <returns>Whether Cross was pressed, or <see langword="null"/> to close on the arrow.</returns>
        private bool? Confirm() => _host._testDriverActive ? ProbeConfirm?.Invoke() : _host._input.State.ConsumeActionPressed();

        /// <summary>Ends a finished call: the camera goes back to the battle, the flow message the caller replaced is
        /// dismissed, and the next command turn starts a fresh command camera as the source command menu creates
        /// its own shot camera.</summary>
        /// <param name="flow">Active battle flow.</param>
        private void Release(BattleFlowController flow)
        {
            Cancel();
            flow.DismissMessage();
            _host._battlePlayerCameraActive = false;
        }

        /// <summary>Drops the entrance call and returns camera and visibility ownership to the battle.</summary>
        private void Cancel()
        {
            _call = null;
            _host._battleRenderer?.ActorScene.Active = false;
        }

        /// <summary>Starts the source entrance call when another party slot or another enemy slot came in, keeping the
        /// outgoing model until the flash covers the screen. A form change of the same slot (digivolution, blast,
        /// return) is not an entrance and keeps the immediate rebuild until its own change sequence exists.</summary>
        /// <param name="flow">Active battle flow, already mutated.</param>
        /// <param name="renderer">Battle renderer.</param>
        /// <param name="changedByAction">Whether the change ended an action sequence.</param>
        /// <returns>Whether an entrance call started.</returns>
        private bool TryBeginEntrance(BattleFlowController flow, BattleRenderer renderer, bool changedByAction)
        {
            var rendered = new BattleRenderedPair(_renderedPlayerSlot, _host._battleRenderedPlayerId, _renderedEnemySlot, _host._battleRenderedEnemyId);
            BattleEntranceKind kind = BattleEntranceTrigger.Resolve(flow.State, rendered, changedByAction);
            bool enemy = kind is BattleEntranceKind.EnemySwitch or BattleEntranceKind.EnemyReplacement;
            if (kind == BattleEntranceKind.None || (enemy ? flow.State.ActiveEnemyFighter : flow.State.ActivePlayerFighter) is not { } entering)
            {
                return false;
            }

            int playerId = flow.State.ActivePlayerFighter?.Id ?? 0;
            int enemyId = flow.State.ActiveEnemyFighter?.Id ?? 0;
            int outgoingId = enemy ? rendered.EnemyId : rendered.PlayerId;
            int idle = BattleEntranceTrigger.Idle(kind, entering);
            var extras = BattleEntranceExtras.For(entering.Id);
            var stage = new BattleRendererEntranceStage(renderer, enemy, () => Rebuild(renderer, flow, playerId, enemyId), _host.ResolveBattleStagePath, _host._log.Warn);
            var (before, after) = BattleEntranceMessages.For(kind, flow.Text, _host.NameLine(outgoingId).Tokens, _host.NameLine(entering.Id).Tokens);
            renderer.StopShotCamera();
            _outgoingSlot = enemy ? rendered.EnemySlot : rendered.PlayerSlot;
            _call = new BattleEntranceCall(new BattleEntranceSequence(enemy, idle, stage, extras), before, after, () => renderer.ActorScene.Active = true, afterAtSwap: kind == BattleEntranceKind.EnemySwitch);
            QuietLog.Info($"battle-entrance begin kind={kind} enemy={enemy} outgoing={outgoingId} incoming={entering.Id} idle={idle} hold={extras.HoldFrames}");
            return true;
        }

        /// <summary>Builds the active pair's models and records them as rendered.</summary>
        /// <param name="renderer">Battle renderer.</param>
        /// <param name="flow">Active battle flow, whose slots are recorded with the rendered pair.</param>
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
            _renderedEnemySlot = flow.State.ActiveEnemy;
            QuietLog.Info($"battle-model-replaced player={playerId} enemy={enemyId}");
        }
    }
}
