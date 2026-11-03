using Dw3Production.Conversion;
using Dw3Production.Logging;
using Dw3Production.Simulation;

namespace Dw3Production;

/// <summary>Contains GameHost's battle-menu coordination implementation.</summary>
public partial class GameHost
{
    /// <summary>Owns the battle-menu branches while using the host only for composed runtime services.</summary>
    private sealed class BattleMenuCoordinator
    {
        private readonly GameHost _host;

        /// <summary>Initializes a new instance of the <see cref="BattleMenuCoordinator"/> class.</summary>
        /// <param name="host">Composed runtime services and battle state.</param>
        public BattleMenuCoordinator(GameHost host)
        {
            _host = host;
        }

        /// <summary>Refreshes commands for the active partner after a switch or digivolution.</summary>
        public void RefreshActivePartnerCatalogs()
        {
            BattleFlowController flow = _host.BattleFlow;
            BattleFighter? active = flow.State.ActivePlayerFighter;
            if (active is null || active.Id == _host._battleCatalogForId)
            {
                return;
            }

            _host._battleCatalogForId = active.Id;
            flow.PlayerBasicTech = BattleSetupFactory.PlayerBasicTech(_host.FighterData, _host.TechniqueData, active.Id);
            flow.PlayerTechniques = BattleSetupFactory.PlayerTechniques(_host.FighterData, _host.TechniqueData, active.Id, flow.Text ?? throw new InvalidOperationException("battle text unavailable"));
            int partnerIndex = BattleSetupFactory.DigimonIndexFor(_host.FighterData, active.Id);
            flow.PlayerDigivolutions = partnerIndex >= 0
                ? BattleSetupFactory.DigivolutionForms(_host.DigivolutionData, partnerIndex, TestTeamGrowth.Level)
                : [];
        }

        /// <summary>Injects the decoded item catalogue and debug stock into a new battle flow.</summary>
        /// <param name="flow">Newly constructed battle flow.</param>
        public void WireCatalogs(BattleFlowController flow)
        {
            var items = new Dictionary<int, BattleItem>();
            var stock = new Dictionary<int, int>();
            var itemText = new Dictionary<int, BattleTextLine>();
            if (_host._battleItemsData is not null)
            {
                foreach (BattleItemData item in _host._battleItemsData.Items)
                {
                    BattleItem battleItem = BattleSetupFactory.ToItem(item);
                    items[battleItem.Id] = battleItem;
                    stock[battleItem.Id] = 3;
                    itemText[battleItem.Id] = BattleTextCatalog.Tokens(item.Name, $"FSITMNAM id={item.NameId}");
                }
            }

            flow.Items = items;
            flow.ItemStock = stock;
            flow.ItemText = itemText;
        }

        /// <summary>Opens the forced partner-switch page after an active fighter is knocked out.</summary>
        public void OpenForcedSwitchMenu()
        {
            _host._battleSwitchForced = true;
            OpenSubmenu(BattleMenuKind.Switch, SwitchLines(_host.BattleFlow), slot => !_host.BattleFlow.State.Players[slot]!.IsKnockedOut);
        }

        /// <summary>Confirms the selected root command.</summary>
        /// <param name="flow">Active battle flow receiving the chosen command.</param>
        public void ConfirmRoot(BattleFlowController flow)
        {
            switch (_host._battleMenu.Cursor)
            {
                case 0:
                    flow.Choose(BattleCommand.Attack, 0, _host._battleRng);
                    _host._battleRenderer?.SetPlayerMotion(3, restart: true);
                    break;
                case 1:
                    OpenTechniqueMenu(flow);
                    break;
                case 2:
                    OpenDigivolutionMenu(flow);
                    break;
                case 3:
                    OpenSubmenu(BattleMenuKind.Switch, SwitchLines(flow), slot => !flow.State.Players[slot]!.IsKnockedOut);
                    break;
                case 4:
                    OpenItemMenu(flow);
                    break;
                case 5:
                    flow.Choose(BattleCommand.Flee, 0, _host._battleRng);
                    break;
            }
        }

        /// <summary>Opens a submenu and records its semantic arguments.</summary>
        /// <param name="kind">Page kind to open.</param>
        /// <param name="entries">Source-backed labels and arguments.</param>
        /// <param name="enabled">Optional enabled-state predicate.</param>
        /// <param name="line">Optional row presentation factory.</param>
        /// <param name="statusText">Optional status text.</param>
        public void OpenSubmenu(BattleMenuKind kind, IReadOnlyList<(int Arg, BattleTextLine Text)> entries, Func<int, bool>? enabled = null,
            Func<int, BattleTextLine, BattleMenuLine>? line = null, BattleMenuStatusText? statusText = null)
        {
            if (entries.Count == 0)
            {
                return;
            }

            _host._battleSubmenuArgs.Clear();
            var lines = new List<BattleMenuLine>(entries.Count);
            foreach ((int arg, BattleTextLine text) in entries)
            {
                _host._battleSubmenuArgs.Add(arg);
                lines.Add(line?.Invoke(arg, text) ?? new BattleMenuLine(text.Debug, enabled?.Invoke(arg) ?? true, text));
            }

            BattleTextCatalog textCatalog = _host.BattleText();
            _host._battleMenu.Open(kind, lines, previousPageText: kind is BattleMenuKind.Technique or BattleMenuKind.Item ? textCatalog.MenuEntry(0x11) : null,
                nextPageText: kind is BattleMenuKind.Technique or BattleMenuKind.Item ? textCatalog.MenuEntry(0x12) : null, statusText: statusText);
            QuietLog.Info($"battle-menu open={kind} lines={lines.Count}");
        }

        /// <summary>Opens the selected partner's single recovered switch-in form.</summary>
        /// <param name="flow">Active battle flow.</param>
        /// <param name="partner">Selected party slot.</param>
        public void OpenSwitchInMenu(BattleFlowController flow, int partner)
        {
            BattleFighter? fighter = flow.State.Players[partner];
            if (fighter is null || fighter.Id == 0 || fighter.IsKnockedOut)
            {
                return;
            }

            _host._battleSwitchPartner = partner;
            OpenSubmenu(BattleMenuKind.SwitchIn, [(fighter.Id, _host.NameLine(fighter.Id))]);
        }

        /// <summary>Opens the recovered pair-switch confirmation page.</summary>
        /// <param name="flow">Active battle flow.</param>
        /// <param name="incomingId">Selected incoming fighter.</param>
        public void OpenPairSwitchMenu(BattleFlowController flow, int incomingId)
        {
            if (_host._battleSwitchPartner < 0 || flow.State.Players[_host._battleSwitchPartner]?.Id != incomingId)
            {
                return;
            }

            OpenSubmenu(BattleMenuKind.PairSwitch, [(0, _host.BattleText().Command(3))]);
        }

        /// <summary>Handles Circle navigation according to the current menu depth.</summary>
        /// <param name="flow">Active battle flow.</param>
        public void Cancel(BattleFlowController flow)
        {
            switch (_host._battleMenu.Menu)
            {
                case BattleMenuKind.PairSwitch:
                    OpenSwitchInMenu(flow, _host._battleSwitchPartner);
                    break;
                case BattleMenuKind.SwitchIn:
                    OpenSubmenu(BattleMenuKind.Switch, SwitchLines(flow), slot => !flow.State.Players[slot]!.IsKnockedOut);
                    break;
                case BattleMenuKind.Switch:
                    CancelSwitch(flow);
                    break;
                case BattleMenuKind.Technique:
                case BattleMenuKind.Digivolve:
                case BattleMenuKind.Item:
                    _host._battleMenu.OpenRoot(flow.RootMenuLines());
                    QuietLog.Info("battle-menu cancel=root");
                    break;
            }
        }

        /// <summary>Returns selectable non-active partner rows.</summary>
        /// <param name="flow">Active battle flow.</param>
        /// <returns>Partner slot and name rows.</returns>
        public List<(int Slot, BattleTextLine Name)> SwitchLines(BattleFlowController flow)
        {
            var result = new List<(int Slot, BattleTextLine Name)>();
            for (var i = 0; i < flow.State.Players.Length; i++)
            {
                BattleFighter? partner = flow.State.Players[i];
                if (partner is not null && partner.Id != 0 && i != flow.State.ActivePlayer)
                {
                    result.Add((i, _host.NameLine(partner.Id)));
                }
            }

            return result;
        }

        /// <summary>Opens the technique page with MP availability and source text.</summary>
        /// <param name="flow">Active battle flow.</param>
        private void OpenTechniqueMenu(BattleFlowController flow)
        {
            OpenSubmenu(BattleMenuKind.Technique, flow.PlayerTechniques.Select(technique => (technique.Id, technique.Text ?? BattleTextLine.Missing)).ToList(),
                line: (id, text) => BuildTechniqueLine(flow, id, text), statusText: TechniqueStatusText(flow.State.ActivePlayerFighter!));
        }

        /// <summary>Builds one technique row with its affordability and description.</summary>
        /// <param name="flow">Active battle flow.</param>
        /// <param name="id">Technique id.</param>
        /// <param name="text">Decoded technique label.</param>
        /// <returns>Configured menu row.</returns>
        private BattleMenuLine BuildTechniqueLine(BattleFlowController flow, int id, BattleTextLine text)
        {
            BattleTechnique technique = flow.PlayerTechniques.First(candidate => candidate.Id == id);
            BattleFighter fighter = flow.State.ActivePlayerFighter!;
            bool enabled = fighter.Temporary || fighter.Mp >= technique.Tech.Mp;
            BattleTextCatalog catalog = _host.BattleText();
            return new BattleMenuLine(text.Debug, enabled, text, Icon: technique.Tech.Icon + 0x37, Cost: technique.Tech.Mp,
                DescriptionText: enabled ? catalog.SkillInfo(id) : catalog.MenuEntry(0x54), CostLabelText: catalog.MenuEntry(0xD));
        }

        /// <summary>Opens the available digivolution forms for the active partner.</summary>
        /// <param name="flow">Active battle flow.</param>
        private void OpenDigivolutionMenu(BattleFlowController flow)
        {
            OpenSubmenu(BattleMenuKind.Digivolve, flow.PlayerDigivolutions.Select(id => (id, _host.NameLine(id))).ToList(),
                line: (id, text) => new BattleMenuLine(text.Debug, id != flow.State.ActivePlayerFighter!.Id, text));
        }

        /// <summary>Opens the battle-item page with stock and decoded source labels.</summary>
        /// <param name="flow">Active battle flow.</param>
        private void OpenItemMenu(BattleFlowController flow)
        {
            var entries = new List<(int ItemId, BattleTextLine Text)>();
            foreach ((int id, int count) in flow.ItemStock)
            {
                if (count > 0)
                {
                    entries.Add((id, flow.ItemText.TryGetValue(id, out BattleTextLine? text) ? text : BattleTextLine.Missing));
                }
            }

            OpenSubmenu(BattleMenuKind.Item, entries, line: (id, text) => BuildItemLine(flow, id, text),
                statusText: new BattleMenuStatusText(_host.BattleText().MenuEntry(0xC)));
        }

        /// <summary>Builds one item row with stock, icon and decoded description.</summary>
        /// <param name="flow">Active battle flow.</param>
        /// <param name="id">Item id.</param>
        /// <param name="text">Decoded item label.</param>
        /// <returns>Configured menu row.</returns>
        private BattleMenuLine BuildItemLine(BattleFlowController flow, int id, BattleTextLine text)
        {
            bool enabled = flow.ItemStock.TryGetValue(id, out int quantity) && quantity > 0;
            int count = enabled ? quantity : 0;
            int icon = _host.BattleItemsData.Items.First(item => item.Id == id).Category;
            return new BattleMenuLine(text.Debug, enabled, text, Icon: icon, Quantity: count, DescriptionText: _host.BattleText().ItemInfo(id));
        }

        /// <summary>Cancels the switch page unless a knockout made switching mandatory.</summary>
        /// <param name="flow">Active battle flow.</param>
        private void CancelSwitch(BattleFlowController flow)
        {
            if (_host._battleSwitchForced)
            {
                QuietLog.Info("battle-menu cancel=refused forced-switch");
                return;
            }

            _host._battleMenu.OpenRoot(flow.RootMenuLines());
            QuietLog.Info("battle-menu cancel=root");
        }

        /// <summary>Builds MP status labels for the active technique menu.</summary>
        /// <param name="fighter">Active player fighter.</param>
        /// <returns>Decoded status labels.</returns>
        private BattleMenuStatusText TechniqueStatusText(BattleFighter fighter)
        {
            BattleTextCatalog text = _host.BattleText();
            int id = fighter.Mp < 100 ? 0x1A : fighter.Mp < 1000 ? 0xF : 0x24;
            BattleTextLine? currentOverride = fighter.Temporary ? text.MenuEntry(id) : null;
            return new BattleMenuStatusText(text.MenuEntry(0xD), currentOverride, text.MenuEntry(0x10));
        }
    }
}
