using Dw3Production.Logging;
using Dw3Production.Simulation;

namespace Dw3Production;

/// <summary>Contains GameHost's battle-menu driver host contract implementation.</summary>
public partial class GameHost
{
    /// <inheritdoc/>
    bool IBattleMenuDriverHost.WasUpHeld() => _battlePrevUp;

    /// <inheritdoc/>
    bool IBattleMenuDriverHost.WasDownHeld() => _battlePrevDown;

    /// <inheritdoc/>
    void IBattleMenuDriverHost.RememberHeldDirections(bool up, bool down)
    {
        _battlePrevUp = up;
        _battlePrevDown = down;
    }

    /// <inheritdoc/>
    void IBattleMenuDriverHost.RefreshActivePartnerCatalogs() => _battleMenuCoordinator.RefreshActivePartnerCatalogs();

    /// <inheritdoc/>
    void IBattleMenuDriverHost.OpenForcedSwitchMenu() => _battleMenuCoordinator.OpenForcedSwitchMenu();

    /// <inheritdoc/>
    void IBattleMenuDriverHost.CancelMenu() => _battleMenuCoordinator.Cancel(BattleFlow);

    /// <inheritdoc/>
    void IBattleMenuDriverHost.ConfirmRoot() => _battleMenuCoordinator.ConfirmRoot(BattleFlow);

    /// <inheritdoc/>
    void IBattleMenuDriverHost.ChooseTechnique(int argument) => BattleFlow.Choose(BattleCommand.Technique, argument, _battleRng);

    /// <inheritdoc/>
    void IBattleMenuDriverHost.ChooseDigivolution(int argument) => BattleFlow.Choose(BattleCommand.Digivolve, argument, _battleRng);

    /// <inheritdoc/>
    void IBattleMenuDriverHost.OpenSwitchIn(int argument) => _battleMenuCoordinator.OpenSwitchInMenu(BattleFlow, argument);

    /// <inheritdoc/>
    void IBattleMenuDriverHost.OpenPairSwitch(int argument) => _battleMenuCoordinator.OpenPairSwitchMenu(BattleFlow, argument);

    /// <inheritdoc/>
    void IBattleMenuDriverHost.ChooseItem(int argument) => BattleFlow.Choose(BattleCommand.Item, argument, _battleRng);

    /// <inheritdoc/>
    void IBattleMenuDriverHost.ConfirmPairSwitch()
    {
        if (_battleMenu.Cursor != 0 || _battleSwitchPartner < 0)
        {
            return;
        }

        if (BattleFlow.Choose(BattleCommand.Switch, _battleSwitchPartner, _battleRng))
        {
            _battlePendingChangeMotion = 13;
            _battleSwitchForced = false;
            QuietLog.Info($"battle-menu switch player={_battleSwitchPartner} action=5 consumes_turn=true");
            return;
        }

        BattleFighter? target = BattleFlow.State.Players[_battleSwitchPartner];
        _log.Warn(
            "battle-menu-switch-refused",
            $"slot={_battleSwitchPartner} active={BattleFlow.State.ActivePlayer} target={target?.Id} hp={target?.Hp}");
    }
}
