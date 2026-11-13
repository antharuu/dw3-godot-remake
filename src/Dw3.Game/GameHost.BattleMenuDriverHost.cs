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
    void IBattleMenuDriverHost.ConfirmPairSwitch() => BattlePairSwitchConfirmation.Confirm(
        _battleMenu.Cursor,
        _battleSwitchPartner,
        () => BattleFlow.Choose(BattleCommand.Switch, _battleSwitchPartner, _battleRng),
        () => BattleFlow.ChoosePairTechnique(_battleSwitchPartner, _battleRng),
        ConfirmSwitch,
        ConfirmPair,
        LogPairSwitchRefusal);

    /// <summary>Completes an accepted ordinary partner switch.</summary>
    private void ConfirmSwitch()
    {
        _battleSwitchForced = false;
        QuietLog.Info($"battle-menu switch player={_battleSwitchPartner} action=5 consumes_turn=true");
    }

    /// <summary>Completes an accepted source-qualified pair action.</summary>
    private void ConfirmPair()
    {
        _battleSwitchForced = false;
        QuietLog.Info($"battle-menu pair player={_battleSwitchPartner} consumes_turn=true");
    }

    /// <summary>Logs a refused switch or pair mutation without changing menu depth.</summary>
    private void LogPairSwitchRefusal()
    {
        BattleFighter? target = BattleFlow.State.Players[_battleSwitchPartner];
        _log.Warn(
            "battle-menu-pair-switch-refused",
            $"slot={_battleSwitchPartner} active={BattleFlow.State.ActivePlayer} target={target?.Id} hp={target?.Hp}");
    }
}
