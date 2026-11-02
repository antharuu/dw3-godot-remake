using Dw3Production.Conversion;
using Dw3Production.Logging;
using Dw3Production.Presentation;
using Dw3Production.Simulation;

namespace Dw3Production;

/// <summary>Contains GameHost's field-runtime coordination implementation.</summary>
public partial class GameHost
{
    /// <inheritdoc/>
    bool IFieldTickHost.IsInTitle => _inTitle;

    /// <inheritdoc/>
    bool IFieldTickHost.HasField => _floor is not null && _currentZone is not null;

    /// <inheritdoc/>
    bool IFieldTickHost.HasActiveBattle => _activeBattle is not null;

    /// <inheritdoc/>
    bool IFieldTickHost.HasActiveDialogue => _dialogue.IsActive;

    /// <inheritdoc/>
    bool IFieldTickHost.HasActiveTraversal => _climb.IsActive || _drop.IsActive;

    /// <inheritdoc/>
    void IFieldTickHost.TickBattleMode() => _fieldRuntimeCoordinator.TickBattleMode();

    /// <inheritdoc/>
    void IFieldTickHost.TickDialogueMode() => _fieldRuntimeCoordinator.TickDialogueMode();

    /// <inheritdoc/>
    void IFieldTickHost.TickTraversalMode() => _fieldRuntimeCoordinator.TickTraversalMode();

    /// <inheritdoc/>
    void IFieldTickHost.TickLocomotionMode(Action<MovementResult> onMovementRejection) => _fieldRuntimeCoordinator.TickLocomotionMode(onMovementRejection);

    /// <inheritdoc/>
    void IFieldTickHost.TickWarp() => _fieldRuntimeCoordinator.TickWarp();

    /// <inheritdoc/>
    void IFieldTickHost.AdvanceFieldPresentation() => _fieldRuntimeCoordinator.AdvancePresentation();

    /// <inheritdoc/>
    void IInteractiveFrameHost.ShowTitle() => _titleScreen?.Show();

    /// <inheritdoc/>
    void IInteractiveFrameHost.StartDebugBattleIfRequested() => DebugBattleTrigger.TryStart(_input.State, StartDebugBattle);

    /// <inheritdoc/>
    void IInteractiveFrameHost.LogSimulationStall(int droppedTicks, int cap) =>
        _log.Warn("sim-stall", $"simulation stall dropped_ticks={droppedTicks} cap={cap}");

    /// <inheritdoc/>
    void IInteractiveFrameHost.SimulateStep() =>
        SimulateStep(movement => _log.Warn($"move:{movement.Rejection}", $"move reject={movement.Rejection} zone={_state.ZoneId} position=({_state.Position.X:0.##},{_state.Position.Y:0.##})"));

    /// <inheritdoc/>
    void IInteractiveFrameHost.PresentBattle() => _battleRenderer?.Present(BuildBattleView());

    /// <inheritdoc/>
    void IInteractiveFrameHost.PresentField(double delta)
    {
        _renderer?.Present(_state, CurrentZone, _input.F3On, delta, _icon.View);
        _dialogueRenderer?.Present(_dialogue.IsActive ? BuildDialogueView() : (DialogueView?)null);
    }

    /// <summary>Coordinates field-mode operations that need the host's composed controllers and presenters.</summary>
    private sealed class FieldRuntimeCoordinator
    {
        private readonly GameHost _host;

        /// <summary>Initializes a new instance of the <see cref="FieldRuntimeCoordinator"/> class.</summary>
        /// <param name="host">Runtime host that owns field state.</param>
        public FieldRuntimeCoordinator(GameHost host)
        {
            _host = host;
        }

        /// <summary>Starts a climb with the trigger-direction convention and diagnostic log.</summary>
        /// <param name="climb">Decoded climb trigger.</param>
        /// <param name="triggerDirection">Raw trigger direction.</param>
        public void StartClimb(Climb climb, int triggerDirection)
        {
            if (climb.Type == 2)
            {
                _host._climb.StartClimbUp(_host._state, triggerDirection, climb.X, climb.Y, climb.Height);
                QuietLog.Info($"climb start type=up slot={climb.Slot} dir={triggerDirection} pos=({climb.X},{climb.Y}) height={climb.Height}");
                return;
            }

            int direction = triggerDirection == 1 ? 5 : 3;
            _host._climb.StartClimbDown(_host._state, direction, climb.X, climb.Y, climb.Height);
            QuietLog.Info($"climb start type=down slot={climb.Slot} dir={direction} pos=({climb.X},{climb.Y}) height={climb.Height}");
        }

        /// <summary>Commits a loaded zone to collision, encounter, renderer and icon state.</summary>
        /// <param name="zone">Loaded field zone.</param>
        public void ApplyScene(FieldZone zone)
        {
            if (_host._playerData is null || _host._renderer is null)
            {
                throw new InvalidOperationException("renderer resources unavailable");
            }

            _host._currentZone = zone;
            _host._floor = zone.Floor;
            _host._areas = zone.Areas;
            _host._occupants = zone.Occupants;
            _host._encounter.SetBattles(ZoneBattleTable.For(_host._encounterData, zone.Zone));
            IReadOnlyList<NpcRecord>? npcRecords = null;
            if (_host._npc is not null && _host._npc.Zones.TryGetValue(zone.Zone.Id, out IReadOnlyList<NpcRecord>? records))
            {
                npcRecords = records;
            }

            _host._renderer.LoadZone(zone.Zone, _host._playerData, _host.ActionData, frame => _host.ResolvePlayerFrame(frame),
                (set, frame) => _host.ResolveActionFrame(set, frame), npcRecords);
            _host._icon.Reset(_host._state.Position);
            _host._renderer.Present(_host._state, zone, _host._input.F3On);
        }

        /// <summary>Advances battle mode and discards the field F3 edge.</summary>
        public void TickBattleMode()
        {
            _host.TickBattle();
            _ = _host._input.State.ConsumeF3Toggled();
        }

        /// <summary>Advances an active dialogue.</summary>
        public void TickDialogueMode() => _host._dialogue.Tick(_host._input.State, QuietLog.Info, _host._log.Warn);

        /// <summary>Advances a climb or drop and repairs a completed traversal position.</summary>
        public void TickTraversalMode()
        {
            if (_host._climb.IsActive)
            {
                _host._climb.Tick(_host._state, _host._input.State);
            }
            else
            {
                _host._drop.Tick(_host._state, _host._input.State);
            }

            if (!_host._climb.IsActive && !_host._drop.IsActive)
            {
                _host.DepenetrateFromWall();
            }
        }

        /// <summary>Advances movement, action and encounter counting when locomotion owns the tick.</summary>
        /// <param name="onMovementRejection">Receives rejected movement details.</param>
        public void TickLocomotionMode(Action<MovementResult> onMovementRejection)
        {
            if (_host._warp.InputLocked)
            {
                return;
            }

            if (_host._warp.State == WarpState.Idle && _host._input.State.ConsumeActionPressed() && _host.TryStartDialogue())
            {
                return;
            }

            _host._state.IsRunning = !_host._input.State.WalkHeld;
            MovementResult movement = Locomotion.Tick(_host._state, _host._input.State.Direction, _host.Floor, _host._occupants);
            if (movement.Rejection is not null)
            {
                onMovementRejection(movement);
            }
            else if (movement.Moved)
            {
                _host.CountEncounterStep();
            }
        }

        /// <summary>Advances the warp machine with host scene and traversal callbacks.</summary>
        public void TickWarp() => _host._warp.Tick(
            _host._state,
            _host._input.State,
            _host.CurrentZone,
            new WarpCallbacks(_host.ResolveTarget, _host.ApplyScene, QuietLog.Info, _host._log.Warn, _host.StartClimb, _host.StartDrop));

        /// <summary>Advances diagnostic overlay state, animation timing and the player icon.</summary>
        public void AdvancePresentation()
        {
            _ = _host._input.State.ConsumeF3Toggled();
            _host._state.AnimationTicks++;
            _host._icon.Advance(_host._state.Position);
            _host._renderer?.AdvanceSimulation(_host._state, _host.CurrentZone);
        }
    }
}
