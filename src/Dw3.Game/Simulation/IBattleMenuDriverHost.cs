namespace Dw3Production.Simulation;

/// <summary>Supplies battle menu input and command callbacks without coupling the driver to the Godot host.</summary>
public interface IBattleMenuDriverHost
{
    /// <summary>Gets the active battle flow.</summary>
    BattleFlowController Flow { get; }

    /// <summary>Gets the active menu state machine.</summary>
    BattleMenuController Menu { get; }

    /// <summary>Gets semantic input for the current fixed tick.</summary>
    InputState Input { get; }

    /// <summary>Gets the arguments represented by current submenu rows.</summary>
    IReadOnlyList<int> SubmenuArguments { get; }

    /// <summary>Reports whether Up was held on the previous fixed tick.</summary>
    /// <returns>True when Up was held.</returns>
    bool WasUpHeld();

    /// <summary>Reports whether Down was held on the previous fixed tick.</summary>
    /// <returns>True when Down was held.</returns>
    bool WasDownHeld();

    /// <summary>Stores held-direction state for the next press-edge comparison.</summary>
    /// <param name="up">Whether Up is held now.</param>
    /// <param name="down">Whether Down is held now.</param>
    void RememberHeldDirections(bool up, bool down);

    /// <summary>Refreshes command catalogs for the active partner.</summary>
    void RefreshActivePartnerCatalogs();

    /// <summary>Opens the mandatory switch menu after an active-partner knockout.</summary>
    void OpenForcedSwitchMenu();

    /// <summary>Handles Circle according to the current menu depth.</summary>
    void CancelMenu();

    /// <summary>Handles a confirmed root command.</summary>
    void ConfirmRoot();

    /// <summary>Chooses a technique row.</summary>
    /// <param name="argument">Technique id stored for the selected row (absolute index across pages).</param>
    void ChooseTechnique(int argument);

    /// <summary>Chooses a digivolution row.</summary>
    /// <param name="argument">Digimon id of the form stored for the cursor row.</param>
    void ChooseDigivolution(int argument);

    /// <summary>Opens the selected partner's switch-in page.</summary>
    /// <param name="argument">Party slot (0..2) stored for the cursor row.</param>
    void OpenSwitchIn(int argument);

    /// <summary>Opens the pair-switch page, the second page of a switch-in.</summary>
    /// <param name="argument">Value stored for the cursor row of the switch-in page, identifying the incoming partner's Digimon.</param>
    void OpenPairSwitch(int argument);

    /// <summary>Confirms the pair-switch page: swaps partners or uses the pair technique.</summary>
    void ConfirmPairSwitch();

    /// <summary>Chooses an item row.</summary>
    /// <param name="argument">Item id stored for the selected row (absolute index across pages).</param>
    void ChooseItem(int argument);
}
