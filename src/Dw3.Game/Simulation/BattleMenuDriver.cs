using Dw3Production.Logging;

namespace Dw3Production.Simulation;

/// <summary>Consumes semantic battle-menu input and dispatches commands through a narrow host contract.</summary>
public sealed class BattleMenuDriver
{
    private readonly IBattleMenuDriverHost _host;

    /// <summary>Initializes a new instance of the <see cref="BattleMenuDriver"/> class.</summary>
    /// <param name="host">Input, menu state and command callbacks.</param>
    public BattleMenuDriver(IBattleMenuDriverHost host)
    {
        _host = host;
    }

    /// <summary>Consumes one fixed tick while the battle waits for a player command.</summary>
    public void Tick()
    {
        BattleFlowController flow = _host.Flow;
        if (flow.Phase != BattleFlowPhase.PlayerCommand)
        {
            return;
        }

        _host.RefreshActivePartnerCatalogs();
        if (flow.State.ActivePlayerFighter?.IsKnockedOut == true && _host.Menu.Menu == BattleMenuKind.Root)
        {
            _host.OpenForcedSwitchMenu();
        }

        MoveCursorOnPressEdge();
        MovePage();
        ConfirmOrCancel();
    }

    /// <summary>Moves the cursor once per Up or Down press rather than once per held frame.</summary>
    private void MoveCursorOnPressEdge()
    {
        bool up = _host.Input.HeldUp;
        bool down = _host.Input.HeldDown;
        bool upEdge = up && !_host.WasUpHeld();
        bool downEdge = down && !_host.WasDownHeld();
        _host.RememberHeldDirections(up, down);
        if (upEdge)
        {
            _host.Menu.MoveUp();
        }

        if (downEdge)
        {
            _host.Menu.MoveDown();
        }
    }

    /// <summary>Consumes paging edges for long technique and item lists.</summary>
    private void MovePage()
    {
        if (_host.Input.ConsumePreviousPagePressed() && _host.Menu.PreviousPage())
        {
            QuietLog.Info($"battle-menu page first={_host.Menu.First} cursor={_host.Menu.Cursor}");
        }

        if (_host.Input.ConsumeNextPagePressed() && _host.Menu.NextPage())
        {
            QuietLog.Info($"battle-menu page first={_host.Menu.First} cursor={_host.Menu.Cursor}");
        }
    }

    /// <summary>Consumes cancellation first, then confirmation when the current row is enabled.</summary>
    private void ConfirmOrCancel()
    {
        if (_host.Input.ConsumeCancelPressed())
        {
            _host.CancelMenu();
            return;
        }

        if (!_host.Input.ConsumeActionPressed() || !_host.Menu.CanConfirm)
        {
            return;
        }

        ConfirmCurrentMenu();
    }

    /// <summary>Dispatches the selected row according to its menu kind.</summary>
    private void ConfirmCurrentMenu()
    {
        switch (_host.Menu.Menu)
        {
            case BattleMenuKind.Root:
                _host.ConfirmRoot();
                break;
            case BattleMenuKind.Technique:
                DispatchSelected(_host.ChooseTechnique, useSelectedIndex: true);
                break;
            case BattleMenuKind.Digivolve:
                DispatchSelected(_host.ChooseDigivolution, useSelectedIndex: false);
                break;
            case BattleMenuKind.Switch:
                DispatchSelected(_host.OpenSwitchIn, useSelectedIndex: false);
                break;
            case BattleMenuKind.SwitchIn:
                DispatchSelected(_host.OpenPairSwitch, useSelectedIndex: false);
                break;
            case BattleMenuKind.PairSwitch:
                _host.ConfirmPairSwitch();
                break;
            case BattleMenuKind.Item:
                DispatchSelected(_host.ChooseItem, useSelectedIndex: true);
                break;
        }
    }

    /// <summary>Maps a visible cursor row to the associated battle command argument.</summary>
    /// <param name="action">Command callback.</param>
    /// <param name="useSelectedIndex">Whether paging requires the absolute selected index.</param>
    private void DispatchSelected(Action<int> action, bool useSelectedIndex)
    {
        int index = useSelectedIndex ? _host.Menu.SelectedIndex : _host.Menu.Cursor;
        if (_host.SubmenuArguments.Count > index)
        {
            action(_host.SubmenuArguments[index]);
        }
    }
}
